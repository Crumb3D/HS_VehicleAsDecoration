using UnityEngine;
using UnityEngine.Scripting;

// Prefab files do not store vehicles. This block is the part the prefab keeps.
// When the chunk loads, it spawns the shell again from the data stored on the block.
[Preserve]
public class BlockHSVehicleDeco : Block
{
    public string vehicleItem;
    public string spawnClass;

    public BlockHSVehicleDeco()
    {
        HasTileEntity = true;
    }

    public override void Init()
    {
        base.Init();
        vehicleItem = Properties.GetString("VehicleItem");
        spawnClass = Properties.GetString("SpawnClass");
    }

    public override bool IsTileEntitySavedInPrefab()
    {
        return true;
    }

    public override void OnBlockAdded(WorldBase _world, Chunk _chunk, Vector3i _blockPos, BlockValue _blockValue, PlatformUserIdentifierAbs _addedByPlayer)
    {
        base.OnBlockAdded(_world, _chunk, _blockPos, _blockValue, _addedByPlayer);
        if (_blockValue.ischild)
            return;
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
        {
            HSVehicleDecoration.ForgetPlacement(_blockPos);
            return;
        }
        bool justPlaced = HSVehicleDecoration.HasPending(_blockPos);
        HSVehicleDecoration.CapturePlacement(_world, _chunk, _blockPos, _blockValue, vehicleItem);
        if (justPlaced && GameManager.Instance != null && GameManager.Instance.IsEditMode())
        {
            HSVehicleDecoration.MaintainShell(_world, _blockPos, _blockValue, vehicleItem, spawnClass);
            return;
        }
        HSVehicleDecoration.ScheduleShell(_world, _blockPos, blockID);
    }

    public override void OnBlockLoaded(WorldBase _world, Vector3i _blockPos, BlockValue _blockValue)
    {
        base.OnBlockLoaded(_world, _blockPos, _blockValue);
        if (_blockValue.ischild || !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            return;
        HSVehicleDecoration.ScheduleShell(_world, _blockPos, blockID);
    }

    public override void OnBlockRemoved(WorldBase _world, Chunk _chunk, Vector3i _blockPos, BlockValue _blockValue)
    {
        base.OnBlockRemoved(_world, _chunk, _blockPos, _blockValue);
        if (_blockValue.ischild)
            return;
        if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            HSVehicleDecoration.RemoveShell(_world, _blockPos);
        _chunk.RemoveTileEntityAt<TileEntityLight>((World)_world, World.toBlock(_blockPos));
    }

    public override bool UpdateTick(WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, bool _bRandomTick, ulong _ticksIfLoaded, GameRandom _rnd)
    {
        if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer && !_blockValue.ischild)
        {
            BlockValue now = _world.GetBlock(_blockPos.x, _blockPos.y, _blockPos.z);
            if (now.Block is BlockHSVehicleDeco)
                HSVehicleDecoration.MaintainShell(_world, _blockPos, now, vehicleItem, spawnClass);
            _world.GetWBT().AddScheduledBlockUpdate(_blockPos, blockID, 200uL);
        }
        return true;
    }
}
