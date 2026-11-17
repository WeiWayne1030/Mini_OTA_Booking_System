namespace MiniOTA.IntegrationTests;

/// <summary>真實時間加上可以調整的偏移量，讓測試可以「快轉」時間。</summary>
public sealed class AdjustableTimeProvider : TimeProvider
{
    private TimeSpan _offset = TimeSpan.Zero;

    public void Advance(TimeSpan by) => _offset += by;

    public void Reset() => _offset = TimeSpan.Zero;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
}