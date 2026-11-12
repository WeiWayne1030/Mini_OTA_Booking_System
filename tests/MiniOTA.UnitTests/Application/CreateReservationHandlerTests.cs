using MiniOTA.Application.Reservations;
using MiniOTA.Domain;

namespace MiniOTA.UnitTests.Application;

public class CreateReservationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    // xUnit 每個測試都會建立新的測試類別實例，所以這些欄位每次都是全新的
    private readonly FakeReservationRepository _repo = new();
    private readonly FakeInventoryReserver _reserver = new();
    private readonly FakeUnitOfWork _uow = new();

    private CreateReservationHandler CreateHandler() =>
        new(_repo, _reserver, _uow, new FixedTimeProvider(Now), new ReservationOptions());

    private static CreateReservationCommand Command(
        string clientId = "client-1", string key = "key-1", int rooms = 2) =>
        new(clientId, key, RoomTypeId: 1,
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), rooms);

    [Fact]
    public async Task SufficientInventory_CreatesReservationAndCommits() // AC-001
    {
        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(CreateReservationOutcome.Created, result.Outcome);
        Assert.NotNull(result.ReservationId);

        DateTime? expectedDeadline = Now.UtcDateTime.AddMinutes(15);
        Assert.Equal(expectedDeadline, result.PaymentDeadlineUtc);

        Assert.Single(_repo.Reservations);
        Assert.Single(_repo.Records);
        Assert.Equal(1, _uow.SaveCount);

        var transaction = Assert.Single(_uow.Transactions);
        Assert.True(transaction.Committed);
        Assert.True(transaction.Disposed);
    }

    [Fact]
    public async Task PassesRoomTypeStayAndRoomCountToInventoryReserver()
    {
        await CreateHandler().HandleAsync(Command(rooms: 3), CancellationToken.None);

        var call = _reserver.LastCall!.Value;
        Assert.Equal(1, call.RoomTypeId);
        Assert.Equal(new DateOnly(2026, 12, 1), call.Stay.CheckIn);
        Assert.Equal(new DateOnly(2026, 12, 3), call.Stay.CheckOut);
        Assert.Equal(3, call.Rooms);
    }

    [Fact]
    public async Task InsufficientInventory_ReturnsFailureAndWritesNothing() // AC-002
    {
        _reserver.Result = false;

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(CreateReservationOutcome.InsufficientInventory, result.Outcome);
        Assert.Null(result.ReservationId);
        Assert.Empty(_repo.Reservations);
        Assert.Empty(_repo.Records);
        Assert.Equal(0, _uow.SaveCount);

        var transaction = Assert.Single(_uow.Transactions);
        Assert.False(transaction.Committed);
        Assert.True(transaction.Disposed);
    }

    [Fact]
    public async Task SameKeyAndSameRequest_ReplaysOriginalWithoutReservingAgain() // AC-005
    {
        var handler = CreateHandler();

        var first = await handler.HandleAsync(Command(), CancellationToken.None);
        var second = await handler.HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(CreateReservationOutcome.Replayed, second.Outcome);
        Assert.Equal(first.ReservationId, second.ReservationId);
        Assert.Equal(first.PaymentDeadlineUtc, second.PaymentDeadlineUtc);
        Assert.Equal(1, _reserver.CallCount);
        Assert.Single(_repo.Reservations);
    }

    [Fact]
    public async Task SameKeyButDifferentRequest_ReturnsConflict() // BR-019
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Command(rooms: 2), CancellationToken.None);

        var second = await handler.HandleAsync(Command(rooms: 5), CancellationToken.None);

        Assert.Equal(CreateReservationOutcome.IdempotencyKeyConflict, second.Outcome);
        Assert.Equal(1, _reserver.CallCount);
        Assert.Single(_repo.Reservations);
    }

    [Fact]
    public async Task SameKeyFromDifferentClients_AreIndependent() // BR-018
    {
        var handler = CreateHandler();

        var a = await handler.HandleAsync(Command(clientId: "client-A"), CancellationToken.None);
        var b = await handler.HandleAsync(Command(clientId: "client-B"), CancellationToken.None);

        Assert.Equal(CreateReservationOutcome.Created, a.Outcome);
        Assert.Equal(CreateReservationOutcome.Created, b.Outcome);
        Assert.NotEqual(a.ReservationId, b.ReservationId);
    }

    [Fact]
    public async Task SaveChangesFails_DoesNotCommit_SoInventoryIsRolledBack() // BR-011、NFR-004
    {
        _uow.ThrowOnSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateHandler().HandleAsync(Command(), CancellationToken.None));

        var transaction = Assert.Single(_uow.Transactions);
        Assert.False(transaction.Committed);
        Assert.True(transaction.Disposed);
    }

    [Fact]
    public async Task DuplicateKeyDetectedOnSave_RollsBackAndReturnsWinnersResult() // BR-019
    {
        var handler = CreateHandler();

        // 請求 A 先成功
        var winner = await handler.HandleAsync(Command(), CancellationToken.None);

        // 請求 B：一開始查不到 A 的紀錄（模擬兩者同時進來），
        // 扣完庫存、準備寫入時，資料庫才發現主鍵重複
        _repo.HiddenLookups = 1;
        _uow.ThrowDuplicateOnSave = true;

        var loser = await handler.HandleAsync(Command(), CancellationToken.None);

        // B 拿到的是 A 的結果，而不是錯誤
        Assert.Equal(CreateReservationOutcome.Replayed, loser.Outcome);
        Assert.Equal(winner.ReservationId, loser.ReservationId);
        Assert.Equal(winner.PaymentDeadlineUtc, loser.PaymentDeadlineUtc);

        // B 確實嘗試過扣庫存（共 2 次），但它的交易沒有提交，所以那次扣除會被回滾
        Assert.Equal(2, _reserver.CallCount);
        var loserTransaction = _uow.Transactions[1];
        Assert.False(loserTransaction.Committed);
        Assert.True(loserTransaction.Disposed);
    }

    [Fact]
    public async Task InsufficientInventory_ButSameKeyAlreadySucceeded_ReturnsReplayed() // FR-007
    {
        var handler = CreateHandler();
        var winner = await handler.HandleAsync(Command(), CancellationToken.None);

        // B 是 A 的重送：一開始查不到 A 的紀錄，而 A 已經拿走最後的房間，所以扣庫存失敗
        _repo.HiddenLookups = 1;
        _reserver.Result = false;

        var result = await handler.HandleAsync(Command(), CancellationToken.None);

        // 不能回報「庫存不足」，因為這個請求其實已經成功了
        Assert.Equal(CreateReservationOutcome.Replayed, result.Outcome);
        Assert.Equal(winner.ReservationId, result.ReservationId);
        Assert.Single(_repo.Reservations);
    }

    [Theory]
    [InlineData("", "key-1")]
    [InlineData("client-1", "")]
    [InlineData("client-1", "   ")]
    public async Task MissingClientOrIdempotencyKey_ThrowsBeforeStartingTransaction(
        string clientId, string key) // BR-018
    {
        await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(Command(clientId, key), CancellationToken.None));

        Assert.Empty(_uow.Transactions);
        Assert.Equal(0, _reserver.CallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveRoomCount_Throws(int rooms) // BR-003
    {
        await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(Command(rooms: rooms), CancellationToken.None));

        Assert.Equal(0, _reserver.CallCount);
    }

    [Fact]
    public async Task CheckOutNotLaterThanCheckIn_Throws() // BR-002
    {
        var command = new CreateReservationCommand(
            "client-1", "key-1", 1, new DateOnly(2026, 12, 3), new DateOnly(2026, 12, 1), 2);

        await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(command, CancellationToken.None));

        Assert.Equal(0, _reserver.CallCount);
    }
}