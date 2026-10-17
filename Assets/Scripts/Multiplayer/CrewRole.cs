/// <summary>
/// The four player seats in a rescue mission. Each seat is intended to be claimed by one player.
/// </summary>
public enum CrewRole
{
    BoatDriver = 0,
    Rescuer = 1,
    Medic = 2,
    Operator = 3
}

public static class CrewRoleInfo
{
    public const int CrewSize = 4;

    public static string DisplayName(this CrewRole role)
    {
        switch (role)
        {
            case CrewRole.BoatDriver: return "Boat Driver";
            case CrewRole.Rescuer: return "Rescuer";
            case CrewRole.Medic: return "Medic";
            case CrewRole.Operator: return "Operator";
            default: return "Unknown";
        }
    }

    public static bool WorksAtEvacuationCenter(this CrewRole role)
    {
        return role == CrewRole.Medic || role == CrewRole.Operator;
    }
}
