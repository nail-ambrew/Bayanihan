using System.Collections;
using UnityEngine;
using static GameplayTestHelpers;

/// <summary>The player's own body on the Manila map (the Gameplay scene): the shared checks in RescuerViewChecks.</summary>
public class RescuerViewTests : RescuerViewChecks
{
    static readonly Vector3 StreetSpot = new Vector3(-30f, 0f, -6.5f);

    protected override IEnumerator LoadTheMap()
    {
        yield return LoadGameplay();
        ClearTheStage();

        // Put the boat on the open street, clear of the hazards, so the swimmer starts beside it as in play.
        var boat = Boat();
        Teleport(boat, new Vector3(StreetSpot.x, boat.transform.position.y, StreetSpot.z), Vector3.left);
    }
}
