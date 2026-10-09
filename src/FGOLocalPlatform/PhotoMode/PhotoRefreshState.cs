namespace FGOLocalPlatform;

internal sealed class PhotoRefreshState
{
	private bool initialized;

	private bool previousActive;

	private int? previousPid;

	internal bool ShouldRefresh(int? pid, bool active)
	{
		bool result = !initialized || active || previousActive || previousPid != pid;
		initialized = true;
		previousActive = active;
		previousPid = pid;
		return result;
	}
}
