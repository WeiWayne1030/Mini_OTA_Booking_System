using MiniOTA.Application.Reservations;
using MiniOTA.Domain;

namespace MiniOTA.UnitTests.Application;

public class ConfirmReservationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeReservationRepository _repo = new();
    private readonly FakeInventoryReserver _reserver = new();
    private readonly FakeUnitOfWork _uow = new();
    private static readonly TimeSpan PaymentTimeout = TimeSpan.FromMinutes(15);

    // 可以指定「現在」，用來模擬已過付款期限
    private ConfirmReservationHandler CreateHandler(DateTimeOffset? now = null) =>
        new(_repo, _reserver, _uow, new FixedTimeProvider(now ?? Now));

    // 在假的 repository 放一筆「剛建立、還沒付款」的預約（期限是 Now + 15 分鐘）
    private Reservation AddPendingReservation()
    {
        var reservation = Reservation.CreatePending(
            roomTypeId: 1,
            stay: new StayDateRange(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3)),
            roomCount: 2,
            nowUtc: Now.UtcDateTime,
            paymentTimeout: PaymentTimeout);
        _repo.Reservations.Add(reservation);
        return reservation;
    }

    //待付款且在期限內 → Confirmed,交易有 commit,呼叫過庫存確認
    [Fact]
    public async Task PendingReservationWithinDeadline_IsConfirmedAndCommits()
    {
        // Arrange:放一筆「剛建立、還沒付款」的預約進假的 repository
        var reservation = Reservation.CreatePending(
            roomTypeId: 1,
            stay: new StayDateRange(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3)),
            roomCount: 2,
            nowUtc: Now.UtcDateTime,                
            paymentTimeout: TimeSpan.FromMinutes(15));
        _repo.Reservations.Add(reservation);

        // Act
        var result = await CreateHandler().HandleAsync(
            new ConfirmReservationCommand(reservation.Id), CancellationToken.None);

        // Assert
        Assert.Equal(ConfirmReservationOutcome.Confirmed, result.Outcome);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(1, _reserver.ConfirmCallCount);
        Assert.True(_uow.Transactions.Single().Committed);
    }

    //找不到預約
    [Fact]
    public async Task ReservationNotFound_ReturnsNotFound_AndTouchesNothing()
    {
        var result = await CreateHandler().HandleAsync(
            new ConfirmReservationCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ConfirmReservationOutcome.NotFound, result.Outcome);
        Assert.Equal(0, _reserver.ConfirmCallCount);
        Assert.Equal(0, _uow.SaveCount);
        Assert.False(_uow.Transactions.Single().Committed);
    }

    //已確認庫存
    [Fact]
    public async Task AlreadyConfirmed_ReturnsAlreadyConfirmed_WithoutMovingInventoryAgain() // BR-017
    {
        var reservation = AddPendingReservation();
        var handler = CreateHandler();

        await handler.HandleAsync(new ConfirmReservationCommand(reservation.Id), CancellationToken.None);
        var second = await handler.HandleAsync(
            new ConfirmReservationCommand(reservation.Id), CancellationToken.None);

        Assert.Equal(ConfirmReservationOutcome.AlreadyConfirmed, second.Outcome);
        Assert.Equal(reservation.Id, second.ReservationId);
        Assert.Equal(1, _reserver.ConfirmCallCount); // 第二次沒有再轉庫存
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
    }

    //待付款超過時間限制
    [Fact]
    public async Task AfterPaymentDeadline_ReturnsNotConfirmable_AndStaysPending() // FR-003
    {
        var reservation = AddPendingReservation();
        var afterDeadline = Now + PaymentTimeout + TimeSpan.FromMinutes(1);

        var result = await CreateHandler(afterDeadline).HandleAsync(
            new ConfirmReservationCommand(reservation.Id), CancellationToken.None);

        Assert.Equal(ConfirmReservationOutcome.NotConfirmable, result.Outcome);
        Assert.Equal(ReservationStatus.PendingPayment, reservation.Status);
        Assert.Equal(0, _reserver.ConfirmCallCount); // 狀態不合法，不能碰庫存
        Assert.False(_uow.Transactions.Single().Committed);
    }

    //付款邊界值 => 付款時間壓線
    [Fact]
    public async Task ExactlyAtPaymentDeadline_StillConfirms() // 與 Domain 的邊界規則一致
    {
        var reservation = AddPendingReservation();
        var atDeadline = Now + PaymentTimeout;

        var result = await CreateHandler(atDeadline).HandleAsync(
            new ConfirmReservationCommand(reservation.Id), CancellationToken.None);

        Assert.Equal(ConfirmReservationOutcome.Confirmed, result.Outcome);
    }

    //終止狀態不能再付款
    [Fact]
    public async Task CancelledReservation_ReturnsNotConfirmable() // BR-014、BR-015
    {
        var reservation = AddPendingReservation();
        reservation.Cancel(Now.UtcDateTime);

        var result = await CreateHandler().HandleAsync(
            new ConfirmReservationCommand(reservation.Id), CancellationToken.None);

        Assert.Equal(ConfirmReservationOutcome.NotConfirmable, result.Outcome);
        Assert.Equal(0, _reserver.ConfirmCallCount);
    }

    //庫存轉換失敗
    [Fact]
    public async Task InventoryConversionFails_Throws_AndDoesNotCommit() // BR-012
    {
        var reservation = AddPendingReservation();
        _reserver.Result = false; // 模擬庫存轉不動

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().HandleAsync(
                new ConfirmReservationCommand(reservation.Id), CancellationToken.None));

        Assert.Equal(0, _uow.SaveCount);
        var transaction = Assert.Single(_uow.Transactions);
        Assert.False(transaction.Committed);
        Assert.True(transaction.Disposed);
    }

    //commit失敗
    [Fact]
    public async Task SaveChangesFails_DoesNotCommit_SoInventoryMoveIsRolledBack() // BR-012、NFR-004
    {
        var reservation = AddPendingReservation();
        _uow.ThrowOnSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().HandleAsync(
                new ConfirmReservationCommand(reservation.Id), CancellationToken.None));

        var transaction = Assert.Single(_uow.Transactions);
        Assert.False(transaction.Committed);
        Assert.True(transaction.Disposed);
    }
}