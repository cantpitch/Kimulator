using Kimulator.Kim1;

namespace Kimulator.App.Audio;

public sealed record CassetteStatus(TapeState State, double PositionSeconds, double LengthSeconds, bool HasTape);
