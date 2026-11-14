using System.Collections;

/// <summary>
/// The player's own body on the Barangay Mabuhay map, as the loadout screen starts it: the same shared checks as the
/// Manila map (RescuerViewChecks), with the swimmer in the water beside the boat where the map puts them.
/// </summary>
public class MabuhayRescuerViewTests : RescuerViewChecks
{
    protected override IEnumerator LoadTheMap() => MabuhayTestHelpers.LoadMabuhay();

    protected override void AfterTheTest() => MabuhayTestHelpers.Restore();
}
