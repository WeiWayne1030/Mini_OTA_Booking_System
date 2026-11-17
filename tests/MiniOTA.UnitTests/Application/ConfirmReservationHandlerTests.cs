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

    // ---------- 同時付款：輸家回滾後重新判斷（AC-007、BR-016） ----------

    //已確認
    [Fact]
    public async Task InventoryConversionFails_WhenReservationIsNowConfirmed_ReturnsAlreadyConfirmed()
    {
        var reservation = AddPendingReservation();
        _reserver.Result = false; // 輸家的庫存轉換被贏家搶先了

        // 假的 repository 存的是同一個物件：handler 呼叫 Confirm() 之後它已是 Confirmed，
        // 等同於「重新查詢時，資料庫顯示贏家已經確認」
        var result = await CreateHandler().HandleAsync(
            new ConfirmReservationCommand(reservation.Id), CancellationToken.None);

        Assert.Equal(ConfirmReservationOutcome.AlreadyConfirmed, result.Outcome);
        Assert.Equal(reservation.Id, result.ReservationId);

        var transaction = Assert.Single(_uow.Transactions);
        Assert.False(transaction.Committed); // 輸家的交易必須回滾
        Assert.True(transaction.Disposed);
        Assert.Equal(1, _uow.ClearTrackedChangesCount); // 重新查詢之前，要先丟掉過時的記憶體狀態
    }

    //已取消
    [Fact]
    public async Task InventoryConversionFails_WhenReservationWasCancelledMeanwhile_ReturnsNotConfirmable()
    {
        var reservation = AddPendingReservation();
        _reserver.Result = false;
        _reserver.OnTryConfirm = () => reservation.Cancel(Now.UtcDateTime); // 輸家在等待時，別人取消了預約

        var result = await CreateHandler().HandleAsync(
            new ConfirmReservationCommand(reservation.Id), CancellationToken.None);

        Assert.Equal(ConfirmReservationOutcome.NotConfirmable, result.Outcome);
        Assert.False(Assert.Single(_uow.Transactions).Committed);
    }

    //仍是待付款
    [Fact]
    public async Task InventoryConversionFails_WhileReservationIsStillPending_Throws() // 真正的資料不一致
    {
        var reservation = AddPendingReservation();
        _reserver.Result = false;

        // 回滾後重新查詢，資料庫顯示這筆預約「仍是待付款」，這與「庫存轉不動」互相矛盾
        var stillPending = Reservation.CreatePending(
            roomTypeId: 1,
            stay: new StayDateRange(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3)),
            roomCount: 2,
            nowUtc: Now.UtcDateTime,
            paymentTimeout: PaymentTimeout);
        _repo.GetOverride = (_, call) => call == 1 ? reservation : stillPending;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().HandleAsync(
                new ConfirmReservationCommand(reservation.Id), CancellationToken.None));

        Assert.Equal(0, _uow.SaveCount);
        Assert.False(Assert.Single(_uow.Transactions).Committed);
        Assert.Equal(1, _uow.ClearTrackedChangesCount);
    }

    [Fact]
    public async Task SaveChangesConflict_ReturnsAlreadyConfirmed_AndRollsBack() // xmin 並發衝突
    {
        var reservation = AddPendingReservation();
        _uow.ThrowConflictOnSave = true; // 存預約時發現它已經被贏家改過

        var result = await CreateHandler().HandleAsync(
            new ConfirmReservationCommand(reservation.Id), CancellationToken.None);

        Assert.Equal(ConfirmReservationOutcome.AlreadyConfirmed, result.Outcome);
        Assert.False(Assert.Single(_uow.Transactions).Committed);
        Assert.Equal(1, _uow.ClearTrackedChangesCount);
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