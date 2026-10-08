using IoT.Contracts;

namespace Application.Api.Models;

public sealed record HistoryEntry(long Id, bool AppliedToState, LifecycleEvent Event);
