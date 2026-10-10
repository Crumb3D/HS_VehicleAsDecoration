using System.Reflection;
using HarmonyLib;

public class HSVehicleAsDecorationMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        if (!HSGameVersion.AllowLoad("[HSVehicleAsDecoration]"))
            return;
        Log.Out("[HSVehicleAsDecoration] v1.0.5 vehicle shells. They cannot be driven, refueled, or picked up. A hidden block keeps each one in the prefab, mods included.");
        try
        {
            new Harmony("HSVehicleAsDecoration").PatchAll(Assembly.GetExecutingAssembly());
        }
        catch (System.Exception e)
        {
            Log.Error("[HSVehicleAsDecoration] Harmony patch failed. Shells can be driven until this patch loads. " + e);
        }
    }
}
