using System.Reflection;
using HarmonyLib;

public class HSVehicleAsDecorationMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        if (!HSGameVersion.AllowLoad("[HSVehicleAsDecoration]"))
            return;
        Log.Out("[HSVehicleAsDecoration] v1.0.2 vehicle shells. Full wheels and mod slots. No engine, no battery, no refuel. Icons match the real vehicles. Bikes are held upright.");
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
