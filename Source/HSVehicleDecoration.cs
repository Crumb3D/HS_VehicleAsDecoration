using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// Shells are the real vehicle entities, so wheels and mod meshes stay on the
// vanilla path. Drive, Ride, and refuel are turned off. Bikes are held upright.
public static class HSVehicleDecoration
{
    static readonly HashSet<int> lowered = new HashSet<int>();

    public static bool IsShell(EntityVehicle vehicle)
    {
        if (vehicle == null)
            return false;
        Vehicle data = vehicle.GetVehicle();
        if (data == null || data.itemValue == null || data.itemValue.ItemClass == null)
            return false;
        string name = data.itemValue.ItemClass.GetItemName();
        return name != null && name.StartsWith("HSVehicleDeco");
    }

    public static bool IsBike(EntityVehicle vehicle)
    {
        if (!IsShell(vehicle))
            return false;
        string name = vehicle.GetVehicle().itemValue.ItemClass.GetItemName();
        return name.IndexOf("Bicycle") >= 0
            || name.IndexOf("Minibike") >= 0
            || name.IndexOf("Motorcycle") >= 0;
    }

    public static void KeepUpright(EntityVehicle vehicle)
    {
        if (vehicle == null || vehicle.isEntityRemote || vehicle.vehicleRB == null)
            return;
        Rigidbody body = vehicle.vehicleRB;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        if (IsBike(vehicle) && lowered.Add(vehicle.entityId))
            body.centerOfMass = new Vector3(0f, -0.35f, 0f);

        Transform physics = vehicle.PhysicsTransform;
        Quaternion current = physics != null ? physics.rotation : body.rotation;
        Vector3 up = current * Vector3.up;
        if (up.y > 0.98f)
            return;

        Vector3 forward = current * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = current * Vector3.right;
            forward.y = 0f;
        }
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();
        Quaternion upright = Quaternion.LookRotation(forward, Vector3.up);
        body.rotation = upright;
        if (!body.isKinematic)
        {
            Vector3 spin = body.angularVelocity;
            spin.x = 0f;
            spin.z = 0f;
            body.angularVelocity = spin;
        }
        if (physics != null)
            physics.rotation = upright;
        if (vehicle.ModelTransform != null)
            vehicle.ModelTransform.rotation = upright;
        vehicle.qrotation = upright;
        vehicle.rotation = upright.eulerAngles;
    }
}

[HarmonyPatch(typeof(EntityVehicle), "isDriveable")]
public static class HSVehicleDecorationDrivePatch
{
    static void Postfix(EntityVehicle __instance, ref bool __result)
    {
        if (__result && HSVehicleDecoration.IsShell(__instance))
            __result = false;
    }
}

[HarmonyPatch(typeof(EntityVehicle), "needsFuel")]
public static class HSVehicleDecorationFuelPatch
{
    static void Postfix(EntityVehicle __instance, ref bool __result)
    {
        if (HSVehicleDecoration.IsShell(__instance))
            __result = false;
    }
}

[HarmonyPatch(typeof(EntityVehicle), "AddFuelFromInventory")]
public static class HSVehicleDecorationRefuelPatch
{
    static bool Prefix(EntityVehicle __instance, ref bool __result)
    {
        if (!HSVehicleDecoration.IsShell(__instance))
            return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(EntityVehicle), "PostInit")]
public static class HSVehicleDecorationPostInitPatch
{
    static void Postfix(EntityVehicle __instance)
    {
        if (HSVehicleDecoration.IsShell(__instance))
            HSVehicleDecoration.KeepUpright(__instance);
    }
}

[HarmonyPatch(typeof(EntityVehicle), "PhysicsFixedUpdate")]
public static class HSVehicleDecorationUprightPatch
{
    static void Postfix(EntityVehicle __instance)
    {
        if (HSVehicleDecoration.IsShell(__instance))
            HSVehicleDecoration.KeepUpright(__instance);
    }
}

[HarmonyPatch(typeof(XUiC_VehicleFrameWindow), "GetBindingValueInternal")]
public static class HSVehicleDecorationFuelUiPatch
{
    static void Postfix(XUiC_VehicleFrameWindow __instance, ref string value, string bindingName)
    {
        if (__instance.Vehicle == null || !HSVehicleDecoration.IsShell(__instance.Vehicle))
            return;
        if (bindingName == "showfuel")
            value = "false";
        else if (bindingName == "fuel")
            value = Localization.Get("xuiNA");
    }
}
