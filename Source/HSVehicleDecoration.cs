using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Shells are real vehicle entities so wheels and mod meshes stay on the vanilla
// path. Prefab files do not store vehicles, so each shell also places an anchor
// block. The block is what the prefab saves. Take is removed.
public static class HSVehicleDecoration
{
    const string ItemPrefix = "HSVehicleDeco";
    const string AnchorPrefix = "HSVehicleDecoAnchor";
    const float OffsetBias = 8f;
    const float OffsetScale = 1000f;

    static readonly HashSet<int> applied = new HashSet<int>();
    static readonly Dictionary<int, Vector3i> anchors = new Dictionary<int, Vector3i>();
    static readonly Dictionary<int, string> saved = new Dictionary<int, string>();
    static readonly Dictionary<int, Vector3> parked = new Dictionary<int, Vector3>();
    static readonly HashSet<int> wheelsOff = new HashSet<int>();
    static readonly Dictionary<Vector3i, PendingPlace> pending = new Dictionary<Vector3i, PendingPlace>();
    static readonly List<Vector3i> editorSpawn = new List<Vector3i>();
    static bool editorSpawnRunning;

    static FieldInfo modField;
    static FieldInfo cosmeticField;
    static PropertyInfo holdingEntityProperty;
    static FieldInfo holdingEntityField;
    static bool holdingEntityBound;

    public struct PendingPlace
    {
        public Vector3 spawn;
        public float yaw;
        public ItemValue item;
    }

    public static bool IsShell(EntityVehicle vehicle)
    {
        if (vehicle == null)
            return false;
        Vehicle data = vehicle.GetVehicle();
        if (data == null || data.itemValue == null || data.itemValue.ItemClass == null)
            return false;
        string name = data.itemValue.ItemClass.GetItemName();
        return name != null && name.StartsWith(ItemPrefix);
    }

    // 3.2 stores holdingEntity as a field. 3.3 replaced it with a property.
    // A DLL built against 3.3 calls get_holdingEntity, which 3.2 does not have.
    public static EntityAlive HoldingEntity(ItemInventoryData data)
    {
        if (data == null)
            return null;
        if (!holdingEntityBound)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            holdingEntityProperty = typeof(ItemInventoryData).GetProperty("holdingEntity", flags);
            holdingEntityField = typeof(ItemInventoryData).GetField("holdingEntity", flags);
            holdingEntityBound = true;
        }
        if (holdingEntityProperty != null)
            return holdingEntityProperty.GetValue(data, null) as EntityAlive;
        if (holdingEntityField != null)
            return holdingEntityField.GetValue(data) as EntityAlive;
        return null;
    }

    public static bool IsShellItem(ItemValue item)
    {
        if (item == null || item.ItemClass == null)
            return false;
        string name = item.ItemClass.GetItemName();
        return name != null && name.StartsWith(ItemPrefix) && name.IndexOf("Anchor") < 0;
    }

    public static string AnchorBlockName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName) || !itemName.StartsWith(ItemPrefix))
            return null;
        return AnchorPrefix + itemName.Substring(ItemPrefix.Length);
    }

    public static void RememberPlacement(Vector3i blockPos, Vector3 spawn, float yaw, ItemValue item)
    {
        PendingPlace place = new PendingPlace();
        place.spawn = spawn;
        place.yaw = yaw;
        place.item = item != null ? item.Clone() : null;
        pending[blockPos] = place;
    }

    public static void ForgetPlacement(Vector3i blockPos)
    {
        pending.Remove(blockPos);
    }

    public static void CapturePlacement(WorldBase world, Chunk chunk, Vector3i blockPos, BlockValue blockValue, string vehicleItem)
    {
        PendingPlace place;
        bool has = pending.TryGetValue(blockPos, out place);
        if (has)
            pending.Remove(blockPos);

        TileEntityLight existing = chunk.GetTileEntity(World.toBlock(blockPos)) as TileEntityLight;
        if (!has && existing != null && existing.LightType == LightType.Directional)
            return;

        float yaw = has ? place.yaw : (blockValue.rotation & 3) * 90f;
        Vector3 spawn = has ? place.spawn : blockPos.ToVector3() + new Vector3(0.5f, 0.25f, 0.5f);
        ItemValue item = has ? place.item : null;
        WriteAnchor(world, chunk, blockPos, vehicleItem, spawn, yaw, item);
    }

    // The prefab editor sets GameManager.bTickingActive false, so a scheduled
    // block update never arrives and the shell stays invisible.
    public static void ScheduleShell(WorldBase world, Vector3i blockPos, int blockId)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsEditMode())
        {
            QueueEditorSpawn(blockPos);
            return;
        }
        world.GetWBT().AddScheduledBlockUpdate(blockPos, blockId, 20uL);
    }

    public static bool HasPending(Vector3i blockPos)
    {
        return pending.ContainsKey(blockPos);
    }

    static void QueueEditorSpawn(Vector3i blockPos)
    {
        editorSpawn.Add(blockPos);
        if (editorSpawnRunning || GameManager.Instance == null)
            return;
        editorSpawnRunning = true;
        GameManager.Instance.StartCoroutine(SpawnEditorShells());
    }

    static IEnumerator SpawnEditorShells()
    {
        // Prefab.CopyIntoLocal writes the saved tile entity after OnBlockAdded returns.
        yield return null;
        Vector3i[] batch = editorSpawn.ToArray();
        editorSpawn.Clear();
        editorSpawnRunning = false;
        World world = GameManager.Instance != null ? GameManager.Instance.World : null;
        if (world == null)
            yield break;
        for (int i = 0; i < batch.Length; i++)
        {
            Vector3i pos = batch[i];
            BlockValue blockValue = world.GetBlock(pos.x, pos.y, pos.z);
            BlockHSVehicleDeco block = blockValue.Block as BlockHSVehicleDeco;
            if (block == null || blockValue.ischild)
                continue;
            MaintainShell(world, pos, blockValue, block.vehicleItem, block.spawnClass);
        }
    }

    public static void MaintainShell(WorldBase world, Vector3i blockPos, BlockValue blockValue, string vehicleItem, string spawnClass)
    {
        World gameWorld = world as World;
        if (gameWorld == null)
            return;

        TileEntityLight light = GetLight(gameWorld, blockPos);
        EntityVehicle existing = FindShell(gameWorld, blockPos);
        if (existing != null)
        {
            if (light != null && light.LightType == LightType.Directional && applied.Add(existing.entityId))
                ApplySaved(existing, light, blockValue, vehicleItem);
            SyncBlockModel(gameWorld, blockPos);
            return;
        }

        int entityClass = FindEntityClass(spawnClass);
        if (entityClass < 0)
        {
            Log.Warning("[HSVehicleAsDecoration] No entity class " + spawnClass);
            return;
        }

        float yaw;
        Vector3 spawn;
        ItemValue item = BuildItem(vehicleItem, light, blockValue, out spawn, out yaw, blockPos);
        Vector3 rotation = new Vector3(0f, yaw, 0f);
        Entity entity = EntityFactory.CreateEntity(entityClass, spawn, rotation);
        entity.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
        EntityVehicle vehicle = entity as EntityVehicle;
        if (vehicle == null)
            return;
        if (item != null && vehicle.GetVehicle() != null)
        {
            EnsureCosmetic(item);
            vehicle.GetVehicle().SetItemValue(item);
        }
        anchors[vehicle.entityId] = blockPos;
        parked[vehicle.entityId] = spawn;
        if (light != null && light.LightType == LightType.Directional)
            applied.Add(vehicle.entityId);
        saved[vehicle.entityId] = Signature(yaw, item);
        gameWorld.SpawnEntityInWorld(vehicle);
        KeepUpright(vehicle);
        SyncBlockModel(gameWorld, blockPos);
    }

    // The world editor draws a prefab preview from block models. It does not draw entities.
    // The live shell is the one that can take mods, so the block model is hidden while that shell is on screen.
    public static void SyncBlockModel(World world, Vector3i blockPos)
    {
        if (world == null)
            return;
        Chunk chunk = world.GetChunkFromWorldPos(blockPos.x, blockPos.y, blockPos.z) as Chunk;
        if (chunk == null)
            return;
        BlockEntityData data = chunk.GetBlockEntity(blockPos);
        if (data == null || data.transform == null)
            return;
        EntityVehicle shell = FindShell(world, blockPos);
        bool shellVisible = false;
        if (shell != null)
        {
            Renderer[] shellRenderers = shell.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < shellRenderers.Length; i++)
            {
                if (shellRenderers[i] != null && shellRenderers[i].enabled && shellRenderers[i].gameObject.activeInHierarchy)
                {
                    shellVisible = true;
                    break;
                }
            }
            Collider[] blockColliders = data.transform.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < blockColliders.Length; i++)
            {
                if (blockColliders[i] != null)
                    blockColliders[i].enabled = false;
            }
        }
        Renderer[] blockRenderers = data.transform.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < blockRenderers.Length; i++)
        {
            if (blockRenderers[i] != null)
                blockRenderers[i].enabled = !shellVisible;
        }
    }

    public static void RemoveShell(WorldBase world, Vector3i blockPos)
    {
        pending.Remove(blockPos);
        World gameWorld = world as World;
        if (gameWorld == null)
            return;
        List<int> drop = new List<int>();
        foreach (KeyValuePair<int, Vector3i> pair in anchors)
        {
            if (pair.Value != blockPos)
                continue;
            drop.Add(pair.Key);
            EntityVehicle vehicle = gameWorld.GetEntity(pair.Key) as EntityVehicle;
            if (vehicle != null && !vehicle.IsDead())
                vehicle.Kill();
        }
        for (int i = 0; i < drop.Count; i++)
        {
            anchors.Remove(drop[i]);
            applied.Remove(drop[i]);
            saved.Remove(drop[i]);
            parked.Remove(drop[i]);
            wheelsOff.Remove(drop[i]);
        }
    }

    public static void KeepUpright(EntityVehicle vehicle)
    {
        if (vehicle == null || vehicle.isEntityRemote || vehicle.vehicleRB == null)
            return;
        Rigidbody body = vehicle.vehicleRB;
        if (!body.isKinematic)
            body.isKinematic = true;
        body.constraints = RigidbodyConstraints.FreezeAll;
        Transform physics = vehicle.PhysicsTransform;
        if (wheelsOff.Add(vehicle.entityId) && physics != null)
        {
            Collider[] wheels = physics.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] != null)
                    wheels[i].enabled = false;
            }
        }
        Vector3 park;
        if (parked.TryGetValue(vehicle.entityId, out park) && (vehicle.position - park).sqrMagnitude > 0.04f)
            vehicle.SetPosition(park, true);
        SyncFromEntity(vehicle);

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
        if (physics != null)
            physics.rotation = upright;
        if (vehicle.ModelTransform != null)
            vehicle.ModelTransform.rotation = upright;
        vehicle.qrotation = upright;
        vehicle.rotation = upright.eulerAngles;
    }

    static void SyncFromEntity(EntityVehicle vehicle)
    {
        if (!applied.Contains(vehicle.entityId))
            return;
        Vector3i blockPos;
        if (!anchors.TryGetValue(vehicle.entityId, out blockPos))
            return;
        World world = vehicle.world as World;
        if (world == null)
            return;
        Chunk chunk = world.GetChunkFromWorldPos(blockPos.x, blockPos.y, blockPos.z) as Chunk;
        if (chunk == null)
            return;
        TileEntityLight light = chunk.GetTileEntity(World.toBlock(blockPos)) as TileEntityLight;
        if (light == null || light.LightType != LightType.Directional)
            return;

        ItemValue item = vehicle.GetVehicle() != null ? vehicle.GetVehicle().itemValue : null;
        float yaw = vehicle.rotation.y;
        string signature = Signature(yaw, item);
        string previous;
        if (saved.TryGetValue(vehicle.entityId, out previous) && previous == signature)
            return;

        Vector3 spawn = vehicle.position;
        WriteLight(light, chunk, blockPos, spawn, yaw, item);
        saved[vehicle.entityId] = signature;
    }

    static void WriteAnchor(WorldBase world, Chunk chunk, Vector3i blockPos, string vehicleItem, Vector3 spawn, float yaw, ItemValue item)
    {
        if (chunk == null)
            return;
        Vector3i local = World.toBlock(blockPos);
        TileEntityLight light = chunk.GetTileEntity(local) as TileEntityLight;
        if (light == null)
        {
            light = new TileEntityLight(chunk);
            light.localChunkPos = local;
            chunk.AddTileEntity(light);
        }
        if (item == null)
            item = ItemClass.GetItem(vehicleItem, false);
        WriteLight(light, chunk, blockPos, spawn, yaw, item);
    }

    static void WriteLight(TileEntityLight light, Chunk chunk, Vector3i blockPos, Vector3 spawn, float yaw, ItemValue item)
    {
        int yawBits = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f));
        int ox = Encode(spawn.x - blockPos.x);
        int oy = Encode(spawn.y - blockPos.y);
        int oz = Encode(spawn.z - blockPos.z);
        int cosmetic = FirstCosmetic(item);
        int[] mods = ModTypes(item);
        light.LightType = LightType.Directional;
        light.LightIntensity = Pack(yawBits, ox);
        light.LightRange = Pack(oy, oz);
        light.LightAngle = Pack(cosmetic, mods[0]);
        light.Rate = Pack(mods[1], mods[2]);
        light.Delay = Pack(mods[3], mods[4]);
        light.SetModified();
        if (chunk != null)
            chunk.isModified = true;
    }

    static void ApplySaved(EntityVehicle vehicle, TileEntityLight light, BlockValue blockValue, string vehicleItem)
    {
        float yaw;
        Vector3 spawn;
        ItemValue item = BuildItem(vehicleItem, light, blockValue, out spawn, out yaw, anchors.ContainsKey(vehicle.entityId) ? anchors[vehicle.entityId] : Vector3i.zero);
        if (item != null && vehicle.GetVehicle() != null)
        {
            EnsureCosmetic(item);
            vehicle.GetVehicle().SetItemValue(item);
        }
        saved[vehicle.entityId] = Signature(yaw, item);
    }

    static ItemValue BuildItem(string vehicleItem, TileEntityLight light, BlockValue blockValue, out Vector3 spawn, out float yaw, Vector3i blockPos)
    {
        yaw = (blockValue.rotation & 3) * 90f;
        spawn = blockPos.ToVector3() + new Vector3(0.5f, 0.25f, 0.5f);
        ItemValue item = ItemClass.GetItem(vehicleItem, false);
        if (item == null || item.ItemClass == null)
            return item;
        item = item.Clone();
        if (light == null || light.LightType != LightType.Directional)
            return item;

        int yawBits, ox, oy, oz, cosmetic, m0, m1, m2, m3, m4;
        Unpack(light.LightIntensity, out yawBits, out ox);
        Unpack(light.LightRange, out oy, out oz);
        Unpack(light.LightAngle, out cosmetic, out m0);
        Unpack(light.Rate, out m1, out m2);
        Unpack(light.Delay, out m3, out m4);
        yaw = yawBits;
        spawn = blockPos.ToVector3() + new Vector3(Decode(ox), Decode(oy), Decode(oz));
        PutMods(item, new int[] { m0, m1, m2, m3, m4 }, cosmetic);
        return item;
    }

    static EntityVehicle FindShell(World world, Vector3i blockPos)
    {
        List<int> stale = null;
        foreach (KeyValuePair<int, Vector3i> pair in anchors)
        {
            if (pair.Value != blockPos)
                continue;
            EntityVehicle vehicle = world.GetEntity(pair.Key) as EntityVehicle;
            if (vehicle != null && !vehicle.IsDead())
                return vehicle;
            if (stale == null)
                stale = new List<int>();
            stale.Add(pair.Key);
        }
        if (stale != null)
        {
            for (int i = 0; i < stale.Count; i++)
            {
                anchors.Remove(stale[i]);
                applied.Remove(stale[i]);
                saved.Remove(stale[i]);
            }
        }
        return null;
    }

    static int FindEntityClass(string name)
    {
        if (string.IsNullOrEmpty(name) || EntityClass.list == null)
            return -1;
        foreach (KeyValuePair<int, EntityClass> item in EntityClass.list.Dict)
        {
            if (item.Value.entityClassName == name)
                return item.Key;
        }
        return -1;
    }

    static TileEntityLight GetLight(World world, Vector3i blockPos)
    {
        Chunk chunk = world.GetChunkFromWorldPos(blockPos.x, blockPos.y, blockPos.z) as Chunk;
        if (chunk == null)
            return null;
        return chunk.GetTileEntity(World.toBlock(blockPos)) as TileEntityLight;
    }

    static void BindFields()
    {
        if (modField != null)
            return;
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        modField = typeof(ItemValue).GetField("modifications", flags) ?? typeof(ItemValue).GetField("Modifications", flags);
        cosmeticField = typeof(ItemValue).GetField("cosmeticMods", flags) ?? typeof(ItemValue).GetField("CosmeticMods", flags);
    }

    static int TypeId(ItemValue item)
    {
        if (item == null)
            return 0;
        PropertyInfo property = typeof(ItemValue).GetProperty("type");
        if (property != null)
            return (int)property.GetValue(item, null);
        FieldInfo field = typeof(ItemValue).GetField("type");
        if (field != null)
            return (int)field.GetValue(item);
        return 0;
    }

    static ItemValue[] ModsOf(ItemValue item)
    {
        BindFields();
        if (item == null || modField == null)
            return null;
        return modField.GetValue(item) as ItemValue[];
    }

    static ItemValue[] CosmeticsOf(ItemValue item)
    {
        BindFields();
        if (item == null || cosmeticField == null)
            return null;
        return cosmeticField.GetValue(item) as ItemValue[];
    }

    static int[] ModTypes(ItemValue item)
    {
        int[] types = new int[5];
        ItemValue[] mods = ModsOf(item);
        if (mods == null)
            return types;
        int count = mods.Length < 5 ? mods.Length : 5;
        for (int i = 0; i < count; i++)
            types[i] = Fit(TypeId(mods[i]));
        return types;
    }

    static int FirstCosmetic(ItemValue item)
    {
        ItemValue[] cosmetics = CosmeticsOf(item);
        if (cosmetics == null || cosmetics.Length == 0)
            return 0;
        return Fit(TypeId(cosmetics[0]));
    }

    static void EnsureCosmetic(ItemValue item)
    {
        BindFields();
        if (item == null || cosmeticField == null)
            return;
        ItemValue[] cosmetics = cosmeticField.GetValue(item) as ItemValue[];
        if (cosmetics == null || cosmetics.Length == 0)
            cosmeticField.SetValue(item, new ItemValue[1]);
    }

    static void PutMods(ItemValue item, int[] types, int cosmetic)
    {
        BindFields();
        ItemValue[] mods = ModsOf(item);
        if (mods != null)
        {
            int count = mods.Length < types.Length ? mods.Length : types.Length;
            for (int i = 0; i < count; i++)
            {
                if (types[i] > 0)
                    mods[i] = new ItemValue(types[i]);
            }
        }
        ItemValue[] cosmetics = CosmeticsOf(item);
        if (cosmetics != null && cosmetics.Length > 0 && cosmetic > 0)
            cosmetics[0] = new ItemValue(cosmetic);
    }

    static int Fit(int typeId)
    {
        if (typeId <= 0)
            return 0;
        if (typeId > 65535)
        {
            Log.Warning("[HSVehicleAsDecoration] Mod item id " + typeId + " does not fit in the prefab anchor.");
            return 0;
        }
        return typeId;
    }

    static string Signature(float yaw, ItemValue item)
    {
        int[] mods = ModTypes(item);
        return Mathf.RoundToInt(Mathf.Repeat(yaw, 360f)) + ":" + FirstCosmetic(item) + ":"
            + mods[0] + "," + mods[1] + "," + mods[2] + "," + mods[3] + "," + mods[4];
    }

    static int Encode(float value)
    {
        return Mathf.Clamp(Mathf.RoundToInt((value + OffsetBias) * OffsetScale), 0, 65000);
    }

    static float Decode(int encoded)
    {
        return encoded / OffsetScale - OffsetBias;
    }

    static float Pack(int a, int b)
    {
        ushort ua = (ushort)Mathf.Clamp(a, 0, 65535);
        ushort ub = (ushort)Mathf.Clamp(b, 0, 65535);
        byte[] bytes = new byte[4];
        bytes[0] = (byte)(ua & 255);
        bytes[1] = (byte)(ua >> 8);
        bytes[2] = (byte)(ub & 255);
        bytes[3] = (byte)(ub >> 8);
        return BitConverter.ToSingle(bytes, 0);
    }

    static void Unpack(float packed, out int a, out int b)
    {
        byte[] bytes = BitConverter.GetBytes(packed);
        a = bytes[0] | (bytes[1] << 8);
        b = bytes[2] | (bytes[3] << 8);
    }
}

[HarmonyPatch(typeof(ItemActionSpawnVehicle), "ExecuteAction")]
public static class HSVehicleDecorationPlacePatch
{
    static bool Prefix(ItemActionSpawnVehicle __instance, ItemActionData _actionData, bool _bReleased)
    {
        if (!_bReleased)
            return true;
        EntityPlayerLocal player = HSVehicleDecoration.HoldingEntity(_actionData.invData) as EntityPlayerLocal;
        if (player == null)
            return true;
        ItemValue held = player.inventory.holdingItemItemValue;
        if (!HSVehicleDecoration.IsShellItem(held))
            return true;

        float time = Time.time;
        if (time - _actionData.lastUseTime < 2f)
            return false;

        ItemActionSpawnVehicle.ItemActionDataSpawnVehicle data = _actionData as ItemActionSpawnVehicle.ItemActionDataSpawnVehicle;
        if (data == null || !data.ValidPosition)
            return false;

        string anchorName = HSVehicleDecoration.AnchorBlockName(held.ItemClass.GetItemName());
        BlockValue blockValue = Block.GetBlockValue(anchorName);
        if (blockValue.isair)
        {
            Log.Warning("[HSVehicleAsDecoration] Missing anchor block " + anchorName);
            return false;
        }

        World world = player.world as World;
        if (world == null)
            return false;

        Vector3 ground = data.Position;
        Vector3 spawn = ground + new Vector3(0f, 0.25f, 0f);
        Vector3i blockPos = new Vector3i(Utils.Fastfloor(ground.x), Utils.Fastfloor(ground.y), Utils.Fastfloor(ground.z));
        if (!world.GetBlock(blockPos.x, blockPos.y, blockPos.z).isair)
            blockPos.y += 1;
        if (!world.GetBlock(blockPos.x, blockPos.y, blockPos.z).isair)
            return false;

        float yaw = player.rotation.y + 90f;
        int stepped = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 90f) & 3;
        blockValue.rotation = (byte)stepped;
        HSVehicleDecoration.RememberPlacement(blockPos, spawn, yaw, held);
        _actionData.lastUseTime = time;
        world.SetBlockRPC(new BlockChangeInfo(blockPos, blockValue));
        player.RightArmAnimationUse = true;
        player.DropTimeDelay = 0.5f;
        player.inventory.DecHoldingItem(1);
        player.PlayOneShot("placeblock");
        __instance.ClearPreview(data);
        return false;
    }
}

[HarmonyPatch(typeof(VehicleManager), "AddTrackedVehicle")]
public static class HSVehicleDecorationTrackPatch
{
    static bool Prefix(EntityVehicle _vehicle)
    {
        return !HSVehicleDecoration.IsShell(_vehicle);
    }
}

[HarmonyPatch(typeof(VehicleManager), "RemoveTrackedVehicle")]
public static class HSVehicleDecorationUntrackPatch
{
    static bool Prefix(EntityVehicle _vehicle)
    {
        return !HSVehicleDecoration.IsShell(_vehicle);
    }
}

[HarmonyPatch(typeof(EntityVehicle), "InitLocalActivationCommands")]
public static class HSVehicleDecorationTakePatch
{
    static void Prefix(EntityVehicle __instance, ref System.Action<EntityActivationCommand> _addCallback)
    {
        if (!HSVehicleDecoration.IsShell(__instance) || _addCallback == null)
            return;
        System.Action<EntityActivationCommand> inner = _addCallback;
        _addCallback = delegate(EntityActivationCommand command)
        {
            if (command.commandId != "take")
                inner(command);
        };
    }
}

[HarmonyPatch(typeof(EntityVehicle), "OnEntityActivated")]
public static class HSVehicleDecorationTakeUsePatch
{
    static bool Prefix(EntityVehicle __instance, EntityActivationCommand _command)
    {
        if (HSVehicleDecoration.IsShell(__instance) && _command.commandId == "take")
            return false;
        return true;
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
