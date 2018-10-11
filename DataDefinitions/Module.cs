﻿using Newtonsoft.Json;

namespace EddiDataDefinitions
{
    public partial class Module : ResourceBasedLocalizedEDName<Module>
    {
        public enum ModuleMount
        {
            Fixed,
            Gimballed,
            Turreted
        }

        // Definition of the module
        [JsonProperty]
        public int @class { get; set; }
        [JsonProperty]
        public string grade { get; set; }
        [JsonProperty]
        public long value { get; set; } // The undiscounted value

        // Additional definition for some items
        [JsonProperty]
        public int? ShipId { get; set; } // Only for bulkheads
        [JsonProperty]
        public ModuleMount? mount { get; set; } // Only for weapons
        [JsonProperty]
        public int? clipcapacity { get; set; } // Only for weapons
        [JsonProperty]
        public int? hoppercapacity { get; set; } // Only for weapons
        [JsonProperty]
        public int? ammoinclip { get; set; } // Only for weapons
        [JsonProperty]
        public int? ammoinhopper { get; set; } // Only for weapons

        // State of the module
        [JsonProperty]
        public long price { get; set; } // How much we actually paid for it
        [JsonProperty]
        public bool enabled { get; set; }
        [JsonProperty]
        public int priority { get; set; }
        [JsonProperty]
        public int position { get; set; }
        [JsonProperty]
        public decimal power { get; set; }
        [JsonProperty]
        public decimal health { get; set; }
        [JsonProperty]
        public bool modified { get; set; } // If the module has been modified

        // Admin
        // The ID in Elite: Dangerous' database
        [JsonProperty]
        public long EDID { get; set; }
        // The ID in eddb.io
        [JsonProperty]
        public long EDDBID { get; set; }

        // TEMP
        public string EDName { get => edname; }

        public string LocalizedMountName()
        {
            return (mount != null) ? Properties.Modules.ResourceManager.GetString(mount.ToString()) : "";
        }

        public Module() : base("", "")
        {}

        public Module(Module Module) : base(Module.edname, Module.basename)
        {
            this.@class = Module.@class;
            this.grade = Module.grade;
            this.value = Module.value;
            this.ShipId = Module.ShipId;
            this.mount = Module.mount;
            this.clipcapacity = Module.clipcapacity;
            this.hoppercapacity = Module.hoppercapacity;
            this.ammoinclip = Module.ammoinclip;
            this.ammoinhopper = Module.ammoinhopper;
            this.EDID = Module.EDID;
            this.EDDBID = Module.EDDBID;
            this.modified = Module.modified;
        }

        public Module(long EDID, string edname, long EDDBID, string basename, int Class, string Grade, long Value) : base(edname, basename)
        {
            this.EDID = EDID;
            this.EDDBID = EDDBID;
            this.@class = Class;
            this.grade = Grade;
            this.value = Value;
            this.modified = false;
            ModulesByEliteID[EDID] = this;
        }

        // Module definition for a bulkhead - requires ship ID
        public Module(long EDID, string edname, long EDDBID, string basename, int Class, string Grade, long Value, int ShipId) : base(edname, basename)
        {
            this.EDID = EDID;
            this.EDDBID = EDDBID;
            this.@class = Class;
            this.grade = Grade;
            this.value = Value;
            this.ShipId = ShipId;
            this.modified = false;
            ModulesByEliteID[EDID] = this;
        }

        // Module definition for a weapon - requires mount and optional ammo
        public Module(long EDID, string edname, long EDDBID, string basename, int Class, string Grade, long Value, ModuleMount Mount, int? AmmoClipCapacity = null, int? AmmoHopperCapacity = null) : base(edname, basename)
        {
            this.EDID = EDID;
            this.EDDBID = EDDBID;
            this.@class = Class;
            this.grade = Grade;
            this.value = Value;
            this.mount = Mount;
            this.clipcapacity = AmmoClipCapacity;
            this.hoppercapacity = AmmoHopperCapacity;
            this.modified = false;
            ModulesByEliteID[EDID] = this;
        }

        public bool IsPowerPlay()
        {
            return PowerPlayModules.Contains(this.edname);
        }

    }
}
