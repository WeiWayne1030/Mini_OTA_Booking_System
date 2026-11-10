using MiniOTA.Domain;

namespace MiniOTA.UnitTests.Domain;

public class ReservationTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);
    private static readonly DateTime Deadline = Now + Timeout;

    private static Reservation NewPending(int rooms = 2) =>
        Reservation.CreatePending(
            roomTypeId: 1,
            new StayDateRange(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3)),
            rooms,
            Now,
            Timeout);

    [Fact]
    public void CreatePending_StartsAsPendingPaymentWithDeadline()
    {
        var reservation = NewPending(rooms: 2);

        Assert.Equal(ReservationStatus.PendingPayment, reservation.Status);
        Assert.Equal(Deadline, reservation.PaymentDeadlineUtc);
        Assert.Equal(2, reservation.RoomCount);
        Assert.NotEqual(Guid.Empty, reservation.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreatePending_NonPositiveRoomCount_Throws(int rooms) // BR-003
    {
        Assert.Throws<DomainException>(() => NewPending(rooms));
    }

    // ---- Confirm (付款) ----

    [Fact]
    public void Confirm_BeforeDeadline_BecomesConfirmed() // BR-014
    {
        var reservation = NewPending();

        reservation.Confirm(Now.AddMinutes(5));

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
    }

    [Fact]
    public void Confirm_ExactlyAtDeadline_StillSucceeds()
    {
        var reservation = NewPending();

        reservation.Confirm(Deadline);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
    }

    [Fact]
    public void Confirm_AfterDeadline_ThrowsAndStaysPending() // FR-003
    {
        var reservation = NewPending();

        Assert.Throws<InvalidReservationTransitionException>(
            () => reservation.Confirm(Deadline.AddSeconds(1)));

        Assert.Equal(ReservationStatus.PendingPayment, reservation.Status);
    }

    [Fact]
    public void Confirm_AlreadyExpired_Throws() // FR-003
    {
        var reservation = NewPending();
        reservation.Expire(Deadline.AddMinutes(1));

        Assert.Throws<InvalidReservationTransitionException>(
            () => reservation.Confirm(Now));

        Assert.Equal(ReservationStatus.Expired, reservation.Status);
    }

    // ---- Expire (過期) ----

    [Fact]
    public void Expire_AfterDeadline_BecomesExpired() // BR-020
    {
        var reservation = NewPending();

        reservation.Expire(Deadline.AddSeconds(1));

        Assert.Equal(ReservationStatus.Expired, reservation.Status);
    }

    [Fact]
    public void Expire_ExactlyAtDeadline_Throws() // 期限當下還不算過期
    {
        var reservation = NewPending();

        Assert.Throws<InvalidReservationTransitionException>(
            () => reservation.Expire(Deadline));

        Assert.Equal(ReservationStatus.PendingPayment, reservation.Status);
    }

    [Fact]
    public void Expire_ConfirmedReservation_Throws() // BR-021
    {
        var reservation = NewPending();
        reservation.Confirm(Now);

        Assert.Throws<InvalidReservationTransitionException>(
            () => reservation.Expire(Deadline.AddMinutes(1)));

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
    }

    // ---- Cancel (取消) ----

    [Fact]
    public void Cancel_Pending_BecomesCancelled() // BR-014
    {
        var reservation = NewPending();

        reservation.Cancel(Now.AddMinutes(1));

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
    }

    [Fact]
    public void Cancel_Confirmed_BecomesCancelled() // BR-014、BR-015
    {
        var reservation = NewPending();
        reservation.Confirm(Now);

        reservation.Cancel(Now.AddMinutes(1));

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
    }

    [Fact]
    public void Cancel_AlreadyCancelled_Throws() // BR-015：終止狀態
    {
        var reservation = NewPending();
        reservation.Cancel(Now);

        Assert.Throws<InvalidReservationTransitionException>(
            () => reservation.Cancel(Now.AddMinutes(1)));
    }

    [Fact]
    public void Cancel_Expired_Throws() // BR-015：終止狀態
    {
        var reservation = NewPending();
        reservation.Expire(Deadline.AddMinutes(1));

        Assert.Throws<InvalidReservationTransitionException>(
            () => reservation.Cancel(Deadline.AddMinutes(2)));

        Assert.Equal(ReservationStatus.Expired, reservation.Status);
    }

    [Fact]
    public void Transition_UpdatesUpdatedAtUtc()
    {
        var reservation = NewPending();
        var later = Now.AddMinutes(3);

        reservation.Confirm(later);

        Assert.Equal(later, reservation.UpdatedAtUtc);
        Assert.Equal(Now, reservation.CreatedAtUtc);
    }
}