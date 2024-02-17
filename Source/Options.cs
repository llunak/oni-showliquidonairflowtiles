using PeterHan.PLib.Options;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace ShowLiquidOnAirflowTiles
{
    [JsonObject(MemberSerialization.OptIn)]
    [ModInfo("https://github.com/llunak/oni-showliquidonairflowtiles")]
    [ConfigFile(SharedConfigLocation: true)]
    [RestartRequired]
    public sealed class Options : SingletonOptions< Options >, IOptions
    {
        [Option("Solid Solar Panels Foundation", "Make the foundation tiles of solar panels solid.")]
        [JsonProperty]
        public bool SolidSolarPanelsFoundation { get; set; } = true;

        public override string ToString()
        {
            return $"ShowLiquidOnAirflowTiles.Options[solidsolarpanelsfoundation={SolidSolarPanelsFoundation}]";
        }

        public void OnOptionsChanged()
        {
            // 'this' is the Options instance used by the options dialog, so set up
            // the actual instance used by the mod. MemberwiseClone() is enough to copy non-reference data.
            Instance = (Options) this.MemberwiseClone();
        }

        public IEnumerable<IOptionsEntry> CreateOptions()
        {
            return null;
        }
    }
}
