namespace BogDb.Samples.TacticalMessaging.Blazor.Services;

public static class TacticalMessagingSeedData
{
    public static void Seed(TacticalMessagingDbContext ctx)
    {
        if (ctx.Standards.Any()) return;

        // ── Lane A: Standards ─────────────────────────────────────────────────
        var standards = new List<EfStandard>
        {
            new() { ExtId = "STD-L16",  Name = "MIL-STD-6016",  Alias = "Link 16",  Status = "ACTIVE",     GoverningBody = "DoD" },
            new() { ExtId = "STD-L22",  Name = "STANAG 5522",   Alias = "Link 22",  Status = "ACTIVE",     GoverningBody = "NATO" },
            new() { ExtId = "STD-VMF",  Name = "MIL-STD-6017",  Alias = "VMF",      Status = "ACTIVE",     GoverningBody = "DoD" },
            new() { ExtId = "STD-L11",  Name = "MIL-STD-6011",  Alias = "Link 11",  Status = "LEGACY",     GoverningBody = "DoD" },
            new() { ExtId = "STD-APP11",Name = "APP-11",         Alias = "APP-11 Catalog", Status = "ACTIVE", GoverningBody = "NATO" },
        };
        ctx.Standards.AddRange(standards);
        ctx.SaveChanges();

        var editions = new List<EfStandardEdition>
        {
            new() { ExtId = "SE-L16-C",  StandardId = standards[0].Id, EditionLabel = "Rev C",  EffectiveDate = "2019-01-01" },
            new() { ExtId = "SE-L16-D",  StandardId = standards[0].Id, EditionLabel = "Rev D",  EffectiveDate = "2023-06-01" },
            new() { ExtId = "SE-L22-3",  StandardId = standards[1].Id, EditionLabel = "Ed 3",   EffectiveDate = "2020-03-01" },
            new() { ExtId = "SE-VMF-2",  StandardId = standards[2].Id, EditionLabel = "v2",     EffectiveDate = "2018-09-01" },
            new() { ExtId = "SE-L11-A",  StandardId = standards[3].Id, EditionLabel = "Legacy", EffectiveDate = "1995-01-01", Deprecated = true },
            new() { ExtId = "SE-APP11-1",StandardId = standards[4].Id, EditionLabel = "Ed 1",   EffectiveDate = "2017-01-01" },
        };
        ctx.Editions.AddRange(editions);
        ctx.SaveChanges();

        var families = new List<EfMessageFamily>
        {
            new() { ExtId = "MF-L16-J0",  EditionId = editions[0].Id, FamilyDesignator = "J0",     Description = "Network Management" },
            new() { ExtId = "MF-L16-J3",  EditionId = editions[0].Id, FamilyDesignator = "J3",     Description = "Surveillance" },
            new() { ExtId = "MF-L16-J6",  EditionId = editions[0].Id, FamilyDesignator = "J6",     Description = "Intelligence" },
            new() { ExtId = "MF-L16-J7",  EditionId = editions[0].Id, FamilyDesignator = "J7",     Description = "Information Management" },
            new() { ExtId = "MF-L16-J12", EditionId = editions[0].Id, FamilyDesignator = "J12",    Description = "Mission Management" },
            new() { ExtId = "MF-L16-J13", EditionId = editions[0].Id, FamilyDesignator = "J13",    Description = "Electronic Warfare" },
            new() { ExtId = "MF-L22-NTDS",EditionId = editions[2].Id, FamilyDesignator = "L22-NTDS",Description = "NTDS Track Family" },
            new() { ExtId = "MF-L22-SAT", EditionId = editions[2].Id, FamilyDesignator = "L22-SAT",Description = "SATCOM Relay Family" },
            new() { ExtId = "MF-VMF-C2",  EditionId = editions[3].Id, FamilyDesignator = "VMF-C2", Description = "Command and Control" },
            new() { ExtId = "MF-VMF-INT", EditionId = editions[3].Id, FamilyDesignator = "VMF-INTEL",Description = "Intelligence Reports" },
        };
        ctx.Families.AddRange(families);
        ctx.SaveChanges();

        var msgTypes = new List<EfMessageType>
        {
            // Link 16 J0
            new() { ExtId = "MT-J0-0",   FamilyId = families[0].Id, TypeDesignator = "J0.0",   Description = "Initial Entry",            Direction = "TRANSMIT" },
            new() { ExtId = "MT-J0-1",   FamilyId = families[0].Id, TypeDesignator = "J0.1",   Description = "Net Test",                  Direction = "BIDIRECTIONAL" },
            // Link 16 J3
            new() { ExtId = "MT-J3-2",   FamilyId = families[1].Id, TypeDesignator = "J3.2",   Description = "Air Track Message",         Direction = "BIDIRECTIONAL" },
            new() { ExtId = "MT-J3-3",   FamilyId = families[1].Id, TypeDesignator = "J3.3",   Description = "Surface Track Message",     Direction = "BIDIRECTIONAL" },
            new() { ExtId = "MT-J3-5",   FamilyId = families[1].Id, TypeDesignator = "J3.5",   Description = "Land Track / Point Message", Direction = "BIDIRECTIONAL" },
            // Link 16 J6
            new() { ExtId = "MT-J6-0",   FamilyId = families[2].Id, TypeDesignator = "J6.0",   Description = "ELINT Info",                Direction = "TRANSMIT" },
            // Link 16 J7
            new() { ExtId = "MT-J7-0",   FamilyId = families[3].Id, TypeDesignator = "J7.0",   Description = "Track Management",          Direction = "BIDIRECTIONAL" },
            new() { ExtId = "MT-J7-1",   FamilyId = families[3].Id, TypeDesignator = "J7.1",   Description = "Data Update Request",       Direction = "RECEIVE" },
            // Link 16 J12
            new() { ExtId = "MT-J12-0",  FamilyId = families[4].Id, TypeDesignator = "J12.0",  Description = "Mission Assignment",        Direction = "TRANSMIT" },
            new() { ExtId = "MT-J12-3",  FamilyId = families[4].Id, TypeDesignator = "J12.3",  Description = "Controlling Unit Change",    Direction = "BIDIRECTIONAL" },
            // Link 16 J13
            new() { ExtId = "MT-J13-2",  FamilyId = families[5].Id, TypeDesignator = "J13.2",  Description = "EW Allocation",             Direction = "TRANSMIT" },
            // Link 22
            new() { ExtId = "MT-L22-T1", FamilyId = families[6].Id, TypeDesignator = "L22-T1", Description = "NTDS Track Report",         Direction = "BIDIRECTIONAL" },
            new() { ExtId = "MT-L22-T2", FamilyId = families[6].Id, TypeDesignator = "L22-T2", Description = "NTDS Track Update",         Direction = "BIDIRECTIONAL" },
            new() { ExtId = "MT-L22-S1", FamilyId = families[7].Id, TypeDesignator = "L22-S1", Description = "SATCOM Relay Status",       Direction = "TRANSMIT" },
            // VMF
            new() { ExtId = "MT-VMF-C2-001", FamilyId = families[8].Id, TypeDesignator = "VMF-C2-001", Description = "C2 Position Report",    Direction = "BIDIRECTIONAL" },
            new() { ExtId = "MT-VMF-C2-002", FamilyId = families[8].Id, TypeDesignator = "VMF-C2-002", Description = "C2 Status Report",      Direction = "TRANSMIT" },
            new() { ExtId = "MT-VMF-C2-003", FamilyId = families[8].Id, TypeDesignator = "VMF-C2-003", Description = "C2 Order Message",      Direction = "RECEIVE" },
            new() { ExtId = "MT-VMF-INT-001",FamilyId = families[9].Id, TypeDesignator = "VMF-INT-001",Description = "Intelligence Summary",   Direction = "TRANSMIT" },
            new() { ExtId = "MT-VMF-INT-002",FamilyId = families[9].Id, TypeDesignator = "VMF-INT-002",Description = "Target Intel Report",    Direction = "BIDIRECTIONAL" },
            // Extra types to reach >20
            new() { ExtId = "MT-J3-7",   FamilyId = families[1].Id, TypeDesignator = "J3.7",   Description = "Subsurface Track",          Direction = "BIDIRECTIONAL" },
            new() { ExtId = "MT-J12-6",  FamilyId = families[4].Id, TypeDesignator = "J12.6",  Description = "Target Sort",               Direction = "TRANSMIT" },
            new() { ExtId = "MT-J0-2",   FamilyId = families[0].Id, TypeDesignator = "J0.2",   Description = "Network Status",            Direction = "BIDIRECTIONAL" },
            new() { ExtId = "MT-J7-2",   FamilyId = families[3].Id, TypeDesignator = "J7.2",   Description = "Track Correlation",          Direction = "BIDIRECTIONAL" },
            new() { ExtId = "MT-J6-3",   FamilyId = families[2].Id, TypeDesignator = "J6.3",   Description = "SIGINT Report",             Direction = "TRANSMIT" },
        };
        ctx.MessageTypes.AddRange(msgTypes);
        ctx.SaveChanges();

        // Fields — spread across key message types with data categories
        var fields = new List<EfMessageField>();
        void AddFields(EfMessageType mt, params (string extSuffix, string name, string cat, bool mand)[] defs)
        {
            foreach (var (extSuffix, name, cat, mand) in defs)
                fields.Add(new() { ExtId = $"MFL-{mt.ExtId.Replace("MT-","")}-{extSuffix}", MessageTypeId = mt.Id,
                    FieldName = name, DataCategory = cat, Mandatory = mand });
        }
        // J3.2 fields
        AddFields(msgTypes[2],
            ("001", "TRACK_NUMBER",    "IDENTITY",   true),
            ("002", "POSITION_LAT",    "KINEMATICS", true),
            ("003", "POSITION_LON",    "KINEMATICS", true),
            ("004", "SPEED",           "KINEMATICS", true),
            ("005", "HEADING",         "KINEMATICS", true),
            ("006", "ALTITUDE",        "KINEMATICS", false),
            ("007", "IDENTITY",        "IDENTITY",   true),
            ("008", "EXERCISE_IND",    "STATUS",     false),
            ("009", "QUALITY",         "STATUS",     false),
            ("010", "PLATFORM_TYPE",   "IDENTITY",   false));
        // J3.3 fields
        AddFields(msgTypes[3],
            ("001", "TRACK_NUMBER",    "IDENTITY",   true),
            ("002", "POSITION_LAT",    "KINEMATICS", true),
            ("003", "POSITION_LON",    "KINEMATICS", true),
            ("004", "SPEED",           "KINEMATICS", true),
            ("005", "COURSE",          "KINEMATICS", true),
            ("006", "IDENTITY",        "IDENTITY",   true));
        // J12.0 fields
        AddFields(msgTypes[8],
            ("001", "MISSION_ID",      "LINK",       true),
            ("002", "ASSIGN_TO",       "LINK",       true),
            ("003", "MISSION_TYPE",    "STATUS",     true),
            ("004", "PRIORITY",        "STATUS",     false));
        // VMF-C2-001 fields
        AddFields(msgTypes[14],
            ("001", "TRACK_NUMBER",    "IDENTITY",   true),
            ("002", "POSITION_LAT",    "KINEMATICS", true),
            ("003", "POSITION_LON",    "KINEMATICS", true),
            ("004", "UNIT_ID",         "IDENTITY",   false),
            ("005", "DTG",             "STATUS",     true));
        // L22-T1 fields
        AddFields(msgTypes[11],
            ("001", "TRACK_NUMBER",    "IDENTITY",   true),
            ("002", "POSITION_LAT",    "KINEMATICS", true),
            ("003", "POSITION_LON",    "KINEMATICS", true),
            ("004", "BEARING",         "KINEMATICS", false));
        // J7.0 fields
        AddFields(msgTypes[6],
            ("001", "TRACK_REF",       "LINK",       true),
            ("002", "ACTION_CODE",     "STATUS",     true));
        // J0.0 fields
        AddFields(msgTypes[0],
            ("001", "NET_NUMBER",      "LINK",       true),
            ("002", "TIME_SLOT",       "STATUS",     true),
            ("003", "ENTRY_STATUS",    "STATUS",     true));
        // VMF-INT-001 fields
        AddFields(msgTypes[17],
            ("001", "REPORT_ID",       "IDENTITY",   true),
            ("002", "CLASSIFICATION",  "STATUS",     true),
            ("003", "SUMMARY_TEXT",    "IDENTITY",   false));
        // J3.5 fields
        AddFields(msgTypes[4],
            ("001", "TRACK_NUMBER",    "IDENTITY",   true),
            ("002", "POSITION_LAT",    "KINEMATICS", true),
            ("003", "MGRS_COORD",      "KINEMATICS", false));
        // J13.2 fields
        AddFields(msgTypes[10],
            ("001", "EMITTER_ID",      "EMITTER",    true),
            ("002", "FREQ_RANGE",      "EMITTER",    true),
            ("003", "JAMMER_STATUS",   "STATUS",     false));
        ctx.MessageFields.AddRange(fields);
        ctx.SaveChanges();

        // Profiles
        var profiles = new List<EfProfile>
        {
            new() { ExtId = "PRF-L16-NAVAL-J3",  Name = "Naval Surface J3.2 Profile",     MessageTypeId = msgTypes[2].Id,  PlatformClass = "SURFACE" },
            new() { ExtId = "PRF-L16-AIR-J3",    Name = "Air J3.2 Profile",               MessageTypeId = msgTypes[2].Id,  PlatformClass = "AIR" },
            new() { ExtId = "PRF-L16-SUB-J3",    Name = "Subsurface J3.3 Profile",        MessageTypeId = msgTypes[3].Id,  PlatformClass = "SUBSURFACE" },
            new() { ExtId = "PRF-L16-NAVAL-J12",  Name = "Naval Surface Mission Profile", MessageTypeId = msgTypes[8].Id,  PlatformClass = "SURFACE" },
            new() { ExtId = "PRF-L22-NTDS",       Name = "Link 22 NTDS Track Profile",    MessageTypeId = msgTypes[11].Id, PlatformClass = "SURFACE" },
            new() { ExtId = "PRF-VMF-C2-SURFACE",Name = "VMF C2 Surface Profile",         MessageTypeId = msgTypes[14].Id, PlatformClass = "SURFACE" },
            new() { ExtId = "PRF-L16-LAND-J3",   Name = "Land Forces J3.5 Profile",       MessageTypeId = msgTypes[4].Id,  PlatformClass = "LAND" },
            new() { ExtId = "PRF-L16-EW-J13",    Name = "EW Allocation Profile",          MessageTypeId = msgTypes[10].Id, PlatformClass = "AIR" },
        };
        ctx.Profiles.AddRange(profiles);
        ctx.SaveChanges();

        // ── Lane B: System ────────────────────────────────────────────────────
        var components = new List<EfTranslatorComponent>
        {
            new() { ExtId = "TCOMP-001", Name = "Link16_J3_2_TrackMessage_Parser",    Version = "2.4.1", Language = "C",   Status = "ACTIVE" },
            new() { ExtId = "TCOMP-002", Name = "Link16_J3_3_SurfaceTrack_Parser",    Version = "1.8.0", Language = "C++", Status = "ACTIVE" },
            new() { ExtId = "TCOMP-003", Name = "Link16_J12_MissionAssign_Encoder",   Version = "3.1.0", Language = "Ada", Status = "ACTIVE" },
            new() { ExtId = "TCOMP-004", Name = "VMF_C2_Encoder",                     Version = "2.0.3", Language = "C++", Status = "ACTIVE" },
            new() { ExtId = "TCOMP-005", Name = "L22_NTDS_Decoder",                   Version = "1.2.0", Language = "C",   Status = "ACTIVE" },
            new() { ExtId = "TCOMP-006", Name = "Link16_J7_TrackMgmt_Handler",        Version = "1.5.2", Language = "C++", Status = "ACTIVE" },
            new() { ExtId = "TCOMP-007", Name = "VMF_Intel_Report_Formatter",         Version = "1.1.0", Language = "C++", Status = "UNDER_CHANGE" },
            new() { ExtId = "TCOMP-008", Name = "Link16_J13_EW_Processor",            Version = "1.0.0", Language = "Ada", Status = "ACTIVE" },
            new() { ExtId = "TCOMP-LEGACY-001", Name = "Link11_Legacy_Decoder",       Version = "5.0.0", Language = "C",   Status = "DEPRECATED" },
        };
        ctx.Components.AddRange(components);
        ctx.SaveChanges();

        var contracts = new List<EfInterfaceContract>
        {
            new() { ExtId = "IC-001", Name = "Gateway-to-Correlator Track Feed",     Protocol = "UDP/MULTICAST", Version = "2.1" },
            new() { ExtId = "IC-002", Name = "Correlator-to-FireControl Data Link",  Protocol = "MQ",            Version = "1.3" },
            new() { ExtId = "IC-003", Name = "Gateway-to-Mediation Relay",           Protocol = "UDP/MULTICAST", Version = "1.0" },
            new() { ExtId = "IC-004", Name = "MissionPlanning-to-C2 Exchange",       Protocol = "REST",          Version = "3.0" },
            new() { ExtId = "IC-005", Name = "Validation-to-Gateway Feedback",       Protocol = "MQ",            Version = "1.1" },
            new() { ExtId = "IC-006", Name = "Intel-to-Correlator Feed",             Protocol = "UDP/MULTICAST", Version = "1.0" },
        };
        ctx.Contracts.AddRange(contracts);
        ctx.SaveChanges();

        var subsystems = new List<EfSubsystem>
        {
            new() { ExtId = "SS-001", Name = "Tactical Picture Gateway",              Domain = "C2" },
            new() { ExtId = "SS-002", Name = "Track Correlation Service",             Domain = "C2" },
            new() { ExtId = "SS-003", Name = "Fire Control Interface Adapter",        Domain = "FIRE_CONTROL" },
            new() { ExtId = "SS-004", Name = "Comms Mediation Service",               Domain = "C2" },
            new() { ExtId = "SS-005", Name = "Mission Planning Exchange Adapter",     Domain = "C2" },
            new() { ExtId = "SS-006", Name = "Message Validation / Translation Svc",  Domain = "C2" },
        };
        ctx.Subsystems.AddRange(subsystems);
        ctx.SaveChanges();

        var platforms = new List<EfPlatform>
        {
            new() { ExtId = "PLT-SHIP-001", PlatformType = "SURFACE" },
            new() { ExtId = "PLT-SHIP-002", PlatformType = "SURFACE" },
            new() { ExtId = "PLT-AUV-001",  PlatformType = "SUBSURFACE" },
            new() { ExtId = "PLT-HELO-001", PlatformType = "AIR" },
            new() { ExtId = "PLT-FIXED-001",PlatformType = "AIR" },
            new() { ExtId = "PLT-LAND-001", PlatformType = "LAND" },
        };
        ctx.Platforms.AddRange(platforms);
        ctx.SaveChanges();

        // IMPLEMENTS: component → profile
        ctx.ComponentProfiles.AddRange(
            new EfComponentProfile { ComponentId = components[0].Id, ProfileId = profiles[0].Id, ImplementationVersion = "2.4.1" },
            new EfComponentProfile { ComponentId = components[0].Id, ProfileId = profiles[1].Id, ImplementationVersion = "2.4.1" },
            new EfComponentProfile { ComponentId = components[1].Id, ProfileId = profiles[2].Id, ImplementationVersion = "1.8.0" },
            new EfComponentProfile { ComponentId = components[2].Id, ProfileId = profiles[3].Id, ImplementationVersion = "3.1.0" },
            new EfComponentProfile { ComponentId = components[4].Id, ProfileId = profiles[4].Id, ImplementationVersion = "1.2.0" },
            new EfComponentProfile { ComponentId = components[3].Id, ProfileId = profiles[5].Id, ImplementationVersion = "2.0.3" },
            new EfComponentProfile { ComponentId = components[5].Id, ProfileId = profiles[0].Id, ImplementationVersion = "1.5.2" },
            new EfComponentProfile { ComponentId = components[7].Id, ProfileId = profiles[7].Id, ImplementationVersion = "1.0.0" }
        );

        // USES_FIELD: component → field (key fields only)
        var j32Fields = fields.Where(f => f.ExtId.StartsWith("MFL-J3-2-")).ToList();
        var j33Fields = fields.Where(f => f.ExtId.StartsWith("MFL-J3-3-")).ToList();
        var j120Fields = fields.Where(f => f.ExtId.StartsWith("MFL-J12-0-")).ToList();
        var vmfC2Fields = fields.Where(f => f.ExtId.StartsWith("MFL-VMF-C2-001-")).ToList();
        var l22Fields = fields.Where(f => f.ExtId.StartsWith("MFL-L22-T1-")).ToList();
        var j70Fields = fields.Where(f => f.ExtId.StartsWith("MFL-J7-0-")).ToList();

        foreach (var f in j32Fields)
            ctx.ComponentFieldUsages.Add(new() { ComponentId = components[0].Id, FieldId = f.Id, Access = f.Mandatory ? "READ" : "WRITE" });
        foreach (var f in j33Fields)
            ctx.ComponentFieldUsages.Add(new() { ComponentId = components[1].Id, FieldId = f.Id, Access = "READ" });
        foreach (var f in j120Fields)
            ctx.ComponentFieldUsages.Add(new() { ComponentId = components[2].Id, FieldId = f.Id, Access = "WRITE" });
        foreach (var f in vmfC2Fields)
            ctx.ComponentFieldUsages.Add(new() { ComponentId = components[3].Id, FieldId = f.Id, Access = "READ" });
        foreach (var f in l22Fields)
            ctx.ComponentFieldUsages.Add(new() { ComponentId = components[4].Id, FieldId = f.Id, Access = "READ" });
        foreach (var f in j70Fields)
            ctx.ComponentFieldUsages.Add(new() { ComponentId = components[5].Id, FieldId = f.Id, Access = "WRITE" });

        // DEPENDS_ON: subsystem → component
        ctx.SubsystemComponents.AddRange(
            new EfSubsystemComponent { SubsystemId = subsystems[0].Id, ComponentId = components[0].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[0].Id, ComponentId = components[1].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[0].Id, ComponentId = components[4].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[1].Id, ComponentId = components[0].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[1].Id, ComponentId = components[5].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[2].Id, ComponentId = components[2].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[2].Id, ComponentId = components[7].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[3].Id, ComponentId = components[3].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[3].Id, ComponentId = components[4].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[4].Id, ComponentId = components[2].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[4].Id, ComponentId = components[3].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[5].Id, ComponentId = components[0].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[5].Id, ComponentId = components[3].Id, DependencyType = "RUNTIME" },
            new EfSubsystemComponent { SubsystemId = subsystems[5].Id, ComponentId = components[6].Id, DependencyType = "RUNTIME" }
        );

        // ON_PLATFORM: subsystem → platform
        ctx.SubsystemPlatforms.AddRange(
            new EfSubsystemPlatform { SubsystemId = subsystems[0].Id, PlatformId = platforms[0].Id, DeploymentDate = "2022-01-15" },
            new EfSubsystemPlatform { SubsystemId = subsystems[0].Id, PlatformId = platforms[1].Id, DeploymentDate = "2022-06-01" },
            new EfSubsystemPlatform { SubsystemId = subsystems[1].Id, PlatformId = platforms[0].Id, DeploymentDate = "2022-01-15" },
            new EfSubsystemPlatform { SubsystemId = subsystems[1].Id, PlatformId = platforms[3].Id, DeploymentDate = "2023-03-01" },
            new EfSubsystemPlatform { SubsystemId = subsystems[2].Id, PlatformId = platforms[0].Id, DeploymentDate = "2022-01-15" },
            new EfSubsystemPlatform { SubsystemId = subsystems[2].Id, PlatformId = platforms[4].Id, DeploymentDate = "2023-06-01" },
            new EfSubsystemPlatform { SubsystemId = subsystems[3].Id, PlatformId = platforms[0].Id, DeploymentDate = "2022-01-15" },
            new EfSubsystemPlatform { SubsystemId = subsystems[3].Id, PlatformId = platforms[2].Id, DeploymentDate = "2023-01-01" },
            new EfSubsystemPlatform { SubsystemId = subsystems[4].Id, PlatformId = platforms[0].Id, DeploymentDate = "2022-03-01" },
            new EfSubsystemPlatform { SubsystemId = subsystems[4].Id, PlatformId = platforms[5].Id, DeploymentDate = "2023-09-01" },
            new EfSubsystemPlatform { SubsystemId = subsystems[5].Id, PlatformId = platforms[0].Id, DeploymentDate = "2022-01-15" },
            new EfSubsystemPlatform { SubsystemId = subsystems[5].Id, PlatformId = platforms[1].Id, DeploymentDate = "2022-06-01" }
        );

        // ── Lane C: Assurance ─────────────────────────────────────────────────
        var capabilities = new List<EfMissionCapability>
        {
            new() { ExtId = "MCAP-001", Name = "MCAP-TRACK-CORRELATION",  Category = "SA" },
            new() { ExtId = "MCAP-002", Name = "MCAP-FIRE-CONTROL",       Category = "FIRES" },
            new() { ExtId = "MCAP-003", Name = "MCAP-SA-PICTURE",         Category = "SA" },
            new() { ExtId = "MCAP-004", Name = "MCAP-MSG-ROUTING",        Category = "C2" },
            new() { ExtId = "MCAP-005", Name = "MCAP-MISSION-PLANNING",   Category = "C2" },
            new() { ExtId = "MCAP-006", Name = "MCAP-INTEL-FUSION",       Category = "SA" },
        };
        ctx.MissionCapabilities.AddRange(capabilities);
        ctx.SaveChanges();

        // ENABLES: subsystem → capability
        ctx.SubsystemCapabilities.AddRange(
            new EfSubsystemCapability { SubsystemId = subsystems[0].Id, CapabilityId = capabilities[2].Id },
            new EfSubsystemCapability { SubsystemId = subsystems[0].Id, CapabilityId = capabilities[3].Id },
            new EfSubsystemCapability { SubsystemId = subsystems[1].Id, CapabilityId = capabilities[0].Id },
            new EfSubsystemCapability { SubsystemId = subsystems[2].Id, CapabilityId = capabilities[1].Id },
            new EfSubsystemCapability { SubsystemId = subsystems[3].Id, CapabilityId = capabilities[3].Id },
            new EfSubsystemCapability { SubsystemId = subsystems[4].Id, CapabilityId = capabilities[4].Id },
            new EfSubsystemCapability { SubsystemId = subsystems[5].Id, CapabilityId = capabilities[5].Id },
            new EfSubsystemCapability { SubsystemId = subsystems[5].Id, CapabilityId = capabilities[3].Id }
        );

        // Requirements
        var reqs = new List<EfRequirement>
        {
            new() { ExtId = "REQ-L16-001", Statement = "Shall decode J3.2 air track messages within 50ms",         Type = "PERFORMANCE",       Priority = "SHALL", AllocatingStandard = "STD-L16" },
            new() { ExtId = "REQ-L16-002", Statement = "Shall support all mandatory J3.2 fields per MIL-STD-6016", Type = "FUNCTIONAL",        Priority = "SHALL", AllocatingStandard = "STD-L16" },
            new() { ExtId = "REQ-L16-003", Statement = "Shall maintain track continuity across net entry events",   Type = "INTEROPERABILITY",  Priority = "SHALL", AllocatingStandard = "STD-L16" },
            new() { ExtId = "REQ-L16-004", Statement = "Should log exercise indicator when present",                Type = "FUNCTIONAL",        Priority = "SHOULD",AllocatingStandard = "STD-L16" },
            new() { ExtId = "REQ-L16-005", Statement = "Shall decode J3.3 surface track messages",                 Type = "FUNCTIONAL",        Priority = "SHALL", AllocatingStandard = "STD-L16" },
            new() { ExtId = "REQ-L16-006", Statement = "Shall encode J12.0 mission assignment messages",           Type = "FUNCTIONAL",        Priority = "SHALL", AllocatingStandard = "STD-L16" },
            new() { ExtId = "REQ-L16-007", Statement = "Shall handle J7.0 track management commands",              Type = "FUNCTIONAL",        Priority = "SHALL", AllocatingStandard = "STD-L16" },
            new() { ExtId = "REQ-L16-008", Statement = "May support J13.2 EW allocation processing",               Type = "FUNCTIONAL",        Priority = "MAY",   AllocatingStandard = "STD-L16" },
            new() { ExtId = "REQ-L22-001", Statement = "Shall decode L22 NTDS track reports",                      Type = "FUNCTIONAL",        Priority = "SHALL", AllocatingStandard = "STD-L22" },
            new() { ExtId = "REQ-L22-002", Statement = "Shall maintain interop with Link 16 track correlation",    Type = "INTEROPERABILITY",  Priority = "SHALL", AllocatingStandard = "STD-L22" },
            new() { ExtId = "REQ-VMF-001", Statement = "Shall encode VMF C2 position reports",                     Type = "FUNCTIONAL",        Priority = "SHALL", AllocatingStandard = "STD-VMF" },
            new() { ExtId = "REQ-VMF-002", Statement = "Shall format VMF intelligence summary reports",            Type = "FUNCTIONAL",        Priority = "SHALL", AllocatingStandard = "STD-VMF" },
            new() { ExtId = "REQ-VMF-003", Statement = "Should validate DTG fields in VMF messages",               Type = "PERFORMANCE",       Priority = "SHOULD",AllocatingStandard = "STD-VMF" },
            new() { ExtId = "REQ-INTEROP-001", Statement = "Shall perform cross-standard track correlation between L16 and L22", Type = "INTEROPERABILITY", Priority = "SHALL", AllocatingStandard = "STD-L16" },
            new() { ExtId = "REQ-INTEROP-002", Statement = "Shall translate VMF C2 messages to Link 16 J3 format",  Type = "INTEROPERABILITY",  Priority = "SHALL", AllocatingStandard = "STD-VMF" },
            new() { ExtId = "REQ-INTEROP-003", Statement = "Should support graceful degradation from L16 to L22",   Type = "INTEROPERABILITY",  Priority = "SHOULD",AllocatingStandard = "STD-L22" },
        };
        ctx.Requirements.AddRange(reqs);
        ctx.SaveChanges();

        // SATISFIES: component → requirement
        ctx.ComponentRequirements.AddRange(
            new EfComponentRequirement { ComponentId = components[0].Id, RequirementId = reqs[0].Id },
            new EfComponentRequirement { ComponentId = components[0].Id, RequirementId = reqs[1].Id },
            new EfComponentRequirement { ComponentId = components[0].Id, RequirementId = reqs[2].Id },
            new EfComponentRequirement { ComponentId = components[0].Id, RequirementId = reqs[3].Id },
            new EfComponentRequirement { ComponentId = components[1].Id, RequirementId = reqs[4].Id },
            new EfComponentRequirement { ComponentId = components[2].Id, RequirementId = reqs[5].Id },
            new EfComponentRequirement { ComponentId = components[5].Id, RequirementId = reqs[6].Id },
            new EfComponentRequirement { ComponentId = components[7].Id, RequirementId = reqs[7].Id },
            new EfComponentRequirement { ComponentId = components[4].Id, RequirementId = reqs[8].Id },
            new EfComponentRequirement { ComponentId = components[4].Id, RequirementId = reqs[9].Id },
            new EfComponentRequirement { ComponentId = components[3].Id, RequirementId = reqs[10].Id },
            new EfComponentRequirement { ComponentId = components[6].Id, RequirementId = reqs[11].Id },
            new EfComponentRequirement { ComponentId = components[3].Id, RequirementId = reqs[12].Id },
            new EfComponentRequirement { ComponentId = components[0].Id, RequirementId = reqs[13].Id },
            new EfComponentRequirement { ComponentId = components[3].Id, RequirementId = reqs[14].Id },
            new EfComponentRequirement { ComponentId = components[4].Id, RequirementId = reqs[15].Id }
        );

        // Test cases — mix of statuses
        var tests = new List<EfTestCase>
        {
            new() { ExtId = "TC-001", Name = "J3.2 Track Decode Round-Trip",         Method = "SIMULATION",       PassCriteria = "Decoded fields match reference",       Status = "PASSING" },
            new() { ExtId = "TC-002", Name = "J3.2 Mandatory Field Coverage",        Method = "ANALYSIS",         PassCriteria = "All mandatory fields decoded",         Status = "PASSING" },
            new() { ExtId = "TC-003", Name = "Track Continuity Across Net Entry",    Method = "HARDWARE_IN_LOOP", PassCriteria = "Track ID preserved across reentry",    Status = "PASSING" },
            new() { ExtId = "TC-004", Name = "Exercise Indicator Logging",           Method = "SIMULATION",       PassCriteria = "Exercise flag visible in log output",  Status = "PASSING" },
            new() { ExtId = "TC-005", Name = "J3.3 Surface Track Decode",            Method = "SIMULATION",       PassCriteria = "Surface track fields match reference", Status = "PASSING" },
            new() { ExtId = "TC-006", Name = "J12.0 Mission Assignment Encode",      Method = "SIMULATION",       PassCriteria = "Encoded message validates",           Status = "PASSING" },
            new() { ExtId = "TC-007", Name = "J7.0 Track Management Handling",       Method = "SIMULATION",       PassCriteria = "Track commands processed correctly",  Status = "PASSING" },
            new() { ExtId = "TC-008", Name = "J13.2 EW Allocation Processing",       Method = "ANALYSIS",         PassCriteria = "EW allocation parsed correctly",      Status = "NOT_RUN" },
            new() { ExtId = "TC-009", Name = "L22 NTDS Track Decode",                Method = "SIMULATION",       PassCriteria = "NTDS fields decoded correctly",       Status = "PASSING" },
            new() { ExtId = "TC-010", Name = "L16-L22 Interop Track Correlation",    Method = "HARDWARE_IN_LOOP", PassCriteria = "Cross-standard correlations valid",   Status = "FAILING" },
            new() { ExtId = "TC-011", Name = "VMF C2 Position Report Encode",        Method = "SIMULATION",       PassCriteria = "VMF position report validates",       Status = "PASSING" },
            new() { ExtId = "TC-012", Name = "VMF Intel Summary Format",             Method = "ANALYSIS",         PassCriteria = "Intel summary conforms to schema",    Status = "BLOCKED" },
            new() { ExtId = "TC-013", Name = "VMF DTG Validation",                   Method = "SIMULATION",       PassCriteria = "DTG format validated correctly",      Status = "PASSING" },
            new() { ExtId = "TC-014", Name = "Cross-Standard Track Correlation",     Method = "HARDWARE_IN_LOOP", PassCriteria = "L16 and L22 tracks merge",            Status = "FAILING" },
            new() { ExtId = "TC-015", Name = "VMF-to-L16 Translation",              Method = "SIMULATION",       PassCriteria = "VMF C2 maps to J3 format",            Status = "PASSING" },
            new() { ExtId = "TC-016", Name = "L16-to-L22 Degraded Mode Fallback",   Method = "SIMULATION",       PassCriteria = "Graceful fallback verified",          Status = "NOT_RUN" },
        };
        ctx.TestCases.AddRange(tests);
        ctx.SaveChanges();

        // VERIFIED_BY: requirement → test
        ctx.RequirementTests.AddRange(
            new EfRequirementTest { RequirementId = reqs[0].Id,  TestCaseId = tests[0].Id },
            new EfRequirementTest { RequirementId = reqs[1].Id,  TestCaseId = tests[1].Id },
            new EfRequirementTest { RequirementId = reqs[2].Id,  TestCaseId = tests[2].Id },
            new EfRequirementTest { RequirementId = reqs[3].Id,  TestCaseId = tests[3].Id },
            new EfRequirementTest { RequirementId = reqs[4].Id,  TestCaseId = tests[4].Id },
            new EfRequirementTest { RequirementId = reqs[5].Id,  TestCaseId = tests[5].Id },
            new EfRequirementTest { RequirementId = reqs[6].Id,  TestCaseId = tests[6].Id },
            new EfRequirementTest { RequirementId = reqs[7].Id,  TestCaseId = tests[7].Id },
            new EfRequirementTest { RequirementId = reqs[8].Id,  TestCaseId = tests[8].Id },
            new EfRequirementTest { RequirementId = reqs[9].Id,  TestCaseId = tests[9].Id },
            new EfRequirementTest { RequirementId = reqs[10].Id, TestCaseId = tests[10].Id },
            new EfRequirementTest { RequirementId = reqs[11].Id, TestCaseId = tests[11].Id },
            new EfRequirementTest { RequirementId = reqs[12].Id, TestCaseId = tests[12].Id },
            new EfRequirementTest { RequirementId = reqs[13].Id, TestCaseId = tests[13].Id },
            new EfRequirementTest { RequirementId = reqs[14].Id, TestCaseId = tests[14].Id },
            new EfRequirementTest { RequirementId = reqs[15].Id, TestCaseId = tests[15].Id }
        );

        // Evidence artifacts — varying verdicts
        var evidence = new List<EfEvidenceArtifact>
        {
            new() { ExtId = "EA-001", ArtifactType = "TEST_LOG",         DateProduced = "2024-01-15", Verdict = "PASS" },
            new() { ExtId = "EA-002", ArtifactType = "ANALYSIS_REPORT",  DateProduced = "2024-01-20", Verdict = "PASS" },
            new() { ExtId = "EA-003", ArtifactType = "TEST_LOG",         DateProduced = "2024-02-10", Verdict = "PASS" },
            new() { ExtId = "EA-004", ArtifactType = "TEST_LOG",         DateProduced = "2024-02-12", Verdict = "PASS" },
            new() { ExtId = "EA-005", ArtifactType = "TEST_LOG",         DateProduced = "2024-03-01", Verdict = "PASS" },
            new() { ExtId = "EA-006", ArtifactType = "TEST_LOG",         DateProduced = "2024-03-15", Verdict = "PASS" },
            new() { ExtId = "EA-007", ArtifactType = "TEST_LOG",         DateProduced = "2024-03-20", Verdict = "PASS" },
            new() { ExtId = "EA-008", ArtifactType = "ATTESTATION",      DateProduced = "2024-04-01", Verdict = "INCONCLUSIVE" }, // TC-008 NOT_RUN
            new() { ExtId = "EA-009", ArtifactType = "TEST_LOG",         DateProduced = "2024-04-10", Verdict = "PASS" },
            new() { ExtId = "EA-010", ArtifactType = "TEST_LOG",         DateProduced = "2024-04-15", Verdict = "FAIL" },  // TC-010 FAILING
            new() { ExtId = "EA-011", ArtifactType = "TEST_LOG",         DateProduced = "2024-05-01", Verdict = "PASS" },
            new() { ExtId = "EA-012", ArtifactType = "ANALYSIS_REPORT",  DateProduced = "2024-05-10", Verdict = "INCONCLUSIVE" }, // TC-012 BLOCKED
            new() { ExtId = "EA-013", ArtifactType = "TEST_LOG",         DateProduced = "2024-05-15", Verdict = "PASS" },
            new() { ExtId = "EA-014", ArtifactType = "TEST_LOG",         DateProduced = "2024-06-01", Verdict = "FAIL" },  // TC-014 FAILING
            new() { ExtId = "EA-015", ArtifactType = "TEST_LOG",         DateProduced = "2024-06-10", Verdict = "PASS" },
            new() { ExtId = "EA-016", ArtifactType = "ATTESTATION",      DateProduced = "2024-06-15", Verdict = "INCONCLUSIVE" }, // TC-016 NOT_RUN
        };
        ctx.EvidenceArtifacts.AddRange(evidence);
        ctx.SaveChanges();

        // PRODUCES: test → evidence (1:1 for simplicity)
        for (int i = 0; i < 16; i++)
            ctx.TestEvidences.Add(new() { TestCaseId = tests[i].Id, EvidenceId = evidence[i].Id, RunDate = evidence[i].DateProduced });

        // Certification packages
        var certPkgs = new List<EfCertificationPackage>
        {
            new() { ExtId = "CP-001", Name = "Link 16 Naval Interop Package R1",     CertificationAuthority = "SYNTHETIC-JITC",  Status = "APPROVED" },
            new() { ExtId = "CP-002", Name = "Link 16 Naval Interop Package R2",     CertificationAuthority = "SYNTHETIC-JITC",  Status = "IN_PROGRESS" },
            new() { ExtId = "CP-003", Name = "VMF C2 Compliance Package",             CertificationAuthority = "SYNTHETIC-JITC",  Status = "SUBMITTED" },
            new() { ExtId = "CP-004", Name = "Cross-Standard Interop Certification",  CertificationAuthority = "SYNTHETIC-NCIA",  Status = "IN_PROGRESS" },
        };
        ctx.CertificationPackages.AddRange(certPkgs);
        ctx.SaveChanges();

        // SUPPORTS: evidence → cert package
        ctx.EvidenceCertifications.AddRange(
            new EfEvidenceCertification { EvidenceId = evidence[0].Id,  PackageId = certPkgs[0].Id },
            new EfEvidenceCertification { EvidenceId = evidence[1].Id,  PackageId = certPkgs[0].Id },
            new EfEvidenceCertification { EvidenceId = evidence[2].Id,  PackageId = certPkgs[0].Id },
            new EfEvidenceCertification { EvidenceId = evidence[3].Id,  PackageId = certPkgs[0].Id },
            new EfEvidenceCertification { EvidenceId = evidence[4].Id,  PackageId = certPkgs[1].Id },
            new EfEvidenceCertification { EvidenceId = evidence[5].Id,  PackageId = certPkgs[1].Id },
            new EfEvidenceCertification { EvidenceId = evidence[6].Id,  PackageId = certPkgs[1].Id },
            new EfEvidenceCertification { EvidenceId = evidence[8].Id,  PackageId = certPkgs[1].Id },
            new EfEvidenceCertification { EvidenceId = evidence[10].Id, PackageId = certPkgs[2].Id },
            new EfEvidenceCertification { EvidenceId = evidence[12].Id, PackageId = certPkgs[2].Id },
            new EfEvidenceCertification { EvidenceId = evidence[14].Id, PackageId = certPkgs[2].Id },
            new EfEvidenceCertification { EvidenceId = evidence[9].Id,  PackageId = certPkgs[3].Id },
            new EfEvidenceCertification { EvidenceId = evidence[13].Id, PackageId = certPkgs[3].Id }
        );

        // ── Baselines ─────────────────────────────────────────────────────────
        var baselines = new List<EfBaseline>
        {
            new() { ExtId = "BL-Q1-2024", Label = "Q1 2024 Release", SnapshotDate = "2024-03-31", Sealed = true },
            new() { ExtId = "BL-Q2-2024", Label = "Q2 2024 Release", SnapshotDate = "2024-06-30", Sealed = true },
            new() { ExtId = "BL-Q3-2024", Label = "Q3 2024 Release", SnapshotDate = "2024-09-30", Sealed = true },
            new() { ExtId = "BL-Q4-2024", Label = "Q4 2024 Release", SnapshotDate = "2024-12-31", Sealed = true },
        };
        ctx.Baselines.AddRange(baselines);
        ctx.SaveChanges();

        // Baseline entries — which entities are contained in each baseline
        // Build extId lists per baseline for drift scenarios
        var allComponentExtIds = components.Select(c => c.ExtId).ToList();
        var allStdExtIds = standards.Select(s => s.ExtId).ToList();
        var allSubsysExtIds = subsystems.Select(s => s.ExtId).ToList();
        var allReqExtIds = reqs.Select(r => r.ExtId).ToList();

        foreach (var bl in baselines)
        {
            // All standards present in all baselines (except Link 11 only in Q1)
            foreach (var ext in allStdExtIds)
            {
                if (ext == "STD-L11" && bl.ExtId != "BL-Q1-2024") continue;
                ctx.BaselineEntries.Add(new() { BaselineId = bl.Id, EntityExtId = ext, EntityType = "Standard" });
            }
            // All subsystems in all baselines
            foreach (var ext in allSubsysExtIds)
                ctx.BaselineEntries.Add(new() { BaselineId = bl.Id, EntityExtId = ext, EntityType = "Subsystem" });
            // All requirements in all baselines
            foreach (var ext in allReqExtIds)
                ctx.BaselineEntries.Add(new() { BaselineId = bl.Id, EntityExtId = ext, EntityType = "Requirement" });
            // Components: VMF Intel cycles in and out to create sequential drift; legacy decoder retires in Q4
            foreach (var ext in allComponentExtIds)
            {
                if (ext == "TCOMP-LEGACY-001" && bl.ExtId == "BL-Q4-2024") continue;
                if (ext == "TCOMP-007" && (bl.ExtId == "BL-Q1-2024" || bl.ExtId == "BL-Q3-2024")) continue;
                ctx.BaselineEntries.Add(new() { BaselineId = bl.Id, EntityExtId = ext, EntityType = "TranslatorComponent" });
            }
            // Profiles
            foreach (var p in profiles)
            {
                if (p.ExtId == "PRF-L16-EW-J13" && (bl.ExtId == "BL-Q1-2024" || bl.ExtId == "BL-Q2-2024")) continue; // added Q3
                ctx.BaselineEntries.Add(new() { BaselineId = bl.Id, EntityExtId = p.ExtId, EntityType = "Profile" });
            }
        }

        // ── Change Events ─────────────────────────────────────────────────────
        var changes = new List<EfChangeEvent>
        {
            new() { ExtId = "CHG-001", Description = "J3.2 TRACK_NUMBER field encoding update for Rev D compatibility",
                Severity = "HIGH", AffectingStandard = "STD-L16" },
            new() { ExtId = "CHG-002", Description = "VMF C2 position report DTG format change per MIL-STD-6017 update",
                Severity = "MODERATE", AffectingStandard = "STD-VMF" },
        };
        ctx.ChangeEvents.AddRange(changes);
        ctx.SaveChanges();

        // AFFECTS: change → target entities
        var trackNumField = fields.First(f => f.ExtId == "MFL-J3-2-001");
        var vmfDtgField = fields.First(f => f.ExtId == "MFL-VMF-C2-001-005");
        ctx.ChangeEventTargets.AddRange(
            new EfChangeEventTarget { ChangeEventId = changes[0].Id, TargetExtId = trackNumField.ExtId, TargetType = "MessageField", Severity = "HIGH" },
            new EfChangeEventTarget { ChangeEventId = changes[0].Id, TargetExtId = components[0].ExtId, TargetType = "TranslatorComponent", Severity = "HIGH" },
            new EfChangeEventTarget { ChangeEventId = changes[1].Id, TargetExtId = vmfDtgField.ExtId,  TargetType = "MessageField", Severity = "MODERATE" },
            new EfChangeEventTarget { ChangeEventId = changes[1].Id, TargetExtId = components[3].ExtId, TargetType = "TranslatorComponent", Severity = "MODERATE" }
        );

        ctx.SaveChanges();
    }
}
