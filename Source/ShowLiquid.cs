using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace ShowLiquidOnAirflowTiles
{
    // Liquid on top of airflow tiles is rendered strangely, as if the tile were gas, since
    // technically it is set to act like gas, so that it's gas-permeable. Work around the rendering
    // problem by detecting liquid on top or to the side of the tile, and if there's any, switch
    // the tile to not be gas-permeable, which will fix the rendering to act as if it were
    // a normal tile (and since there is liquid, gas cannot really pass through the tile anyway,
    // as least it shouldn't in any meaningful direction).
    // This is obviously a hack, but it seems to work well enough.
    [HarmonyPatch]
    public class AirflowTileLiquidChecker : KMonoBehaviour
    {
        private HandleVector< int >.Handle partitionerEntry;
        private int cell;
        private bool active;

        private static bool onSpawnHack = false;

        private SimCellOccupier simCellOccupier;

        private static readonly AccessTools.FieldRef< object, bool > callDestroyAccess
            = AccessTools.FieldRefAccess< bool >( typeof( SimCellOccupier ), "callDestroy" );

        delegate void OnSpawnDelegate( SimCellOccupier instance );
        private static readonly OnSpawnDelegate onSpawnDelegate
            = AccessTools.MethodDelegate< OnSpawnDelegate >(
                AccessTools.Method( typeof( SimCellOccupier ), "OnSpawn" ));

        protected override void OnSpawn()
        {
            base.OnSpawn();
            simCellOccupier = GetComponent< SimCellOccupier >();
            cell = Grid.PosToCell( gameObject );
            Vector2I vector2I = Grid.CellToXY( cell );
            // Check cell above and both to the sides.
            Extents extents = new Extents( vector2I.x - 1, vector2I.y, 3, 2 );
            partitionerEntry = GameScenePartitioner.Instance.Add( "AirflowTileLiquidChecker.OnSpawn",
                gameObject, extents, GameScenePartitioner.Instance.liquidChangedLayer, OnLiquidChanged );
            CheckLiquid();
        }

        protected override void OnCleanUp()
        {
            GameScenePartitioner.Instance.Free( ref partitionerEntry );
            base.OnCleanUp();
        }

        private void OnLiquidChanged( object data )
        {
            CheckLiquid();
        }

        private void CheckLiquid()
        {
            bool activate = CheckCell( Grid.CellAbove( cell ))
                || CheckCell( Grid.CellLeft( cell ))
                || CheckCell( Grid.CellRight( cell ));
            if( activate == active )
                return;
            active = activate;
            simCellOccupier.doReplaceElement = active;
            // Changing SimCellOccupier's doReplaceElement would be enough, but the effect
            // is applied only in its OnSpawn() and undone DestroySelf() that's called only from OnCleanUp(),
            // so call those.
            simCellOccupier.DestroySelf( null );
            // Undo the only unwanted effect of DestroySelf().
            callDestroyAccess( simCellOccupier ) = true;
            // Block unwanted side effects of OnSpawn().
            onSpawnHack = true;
            onSpawnDelegate( simCellOccupier );
            onSpawnHack = false;
        }

        private bool CheckCell( int checkCell )
        {
            if( !Grid.IsValidCell( checkCell ))
                return false;
            return Grid.IsLiquid( checkCell );
        }

        [HarmonyPatch(typeof(SimCellOccupier))]
        [HarmonyTranspiler]
        [HarmonyPatch("OnSpawn")]
        public static IEnumerable<CodeInstruction> OnSpawnTranspiller(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);
            bool found = false;
            for( int i = 0; i < codes.Count; ++i )
            {
                // Debug.Log("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
                // The function has code:
                // Subscribe(-1699355994, OnBuildingRepairedDelegate);
                // Prepend:
                // if( OnSpawn_Hook())
                //     return;
                if( codes[ i ].opcode == OpCodes.Ldarg_0
                    && i + 5 < codes.Count
                    && codes[ i + 1 ].opcode == OpCodes.Ldc_I4 && codes[ i + 1 ].operand.ToString() == "-1699355994"
                    && codes[ i + 2 ].opcode == OpCodes.Ldsfld && codes[ i + 2 ].operand.ToString().EndsWith( "OnBuildingRepairedDelegate" )
                    && codes[ i + 3 ].opcode == OpCodes.Call
                    && codes[ i + 3 ].operand.ToString().StartsWith( "Int32 Subscribe" )
                    && codes[ i + 4 ].opcode == OpCodes.Pop
                    && codes[ i + 5 ].opcode == OpCodes.Ret )
                {
                    codes.Insert( i, new CodeInstruction( OpCodes.Call,
                        typeof( AirflowTileLiquidChecker ).GetMethod( nameof( OnSpawn_Hook ))));
                    Label label = generator.DefineLabel();
                    codes.Insert( i + 1, new CodeInstruction( OpCodes.Brfalse_S, label ));
                    codes.Insert( i + 2, new CodeInstruction( OpCodes.Ret ));
                    codes[ i + 3 ].labels.Add( label ); // the original first instruction
                    found = true;
                    break;
                }
            }
            if(!found)
                Debug.LogWarning("ShowLiquidOnAirflowTiles: Failed to patch SimCellOccupier.OnSpawn()");
            return codes;
        }

        public static bool OnSpawn_Hook()
        {
            return onSpawnHack;
        }
    }

    [HarmonyPatch(typeof(GasPermeableMembraneConfig))]
    public class GasPermeableMembraneConfig_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ConfigureBuildingTemplate))]
        public static void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.AddOrGet< AirflowTileLiquidChecker >();
        }
    }

    // The lower row of cell of the solar panel acts as a foundation, but again it's not a real tile
    // in some regards, so it's also rendered poorly. Here the tile acting as a solid tile actually
    // generally makes sense, so simply set it that way. A drawback is that heavy-watt wire can
    // no longer pass though those tiles.
    [HarmonyPatch(typeof(SolarPanelConfig))]
    public class SolarPanelConfig_Patch
    {
        [HarmonyPrepare]
        public static bool Prepare() => Options.Instance.SolidSolarPanelsFoundation;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(DoPostConfigureComplete))]
        public static void DoPostConfigureComplete(GameObject go)
        {
            MakeBaseSolid.Def def = go.AddOrGetDef<MakeBaseSolid.Def>();
            def.occupyFoundationLayer = true;
        }
    }
}
