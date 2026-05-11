using Microsoft.EntityFrameworkCore;

namespace BogDb.Samples.TacticalMessaging.Blazor.Services;

// ── Lane A: Standards ──────────────────────────────────────────────────────────

public class EfStandard
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";          // STD-L16
    public string Name { get; set; } = "";            // MIL-STD-6016
    public string Alias { get; set; } = "";           // Link 16
    public string Status { get; set; } = "ACTIVE";    // ACTIVE|DEPRECATED|LEGACY
    public string GoverningBody { get; set; } = "";   // DoD|NATO
    public List<EfStandardEdition> Editions { get; set; } = [];
}

public class EfStandardEdition
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // SE-L16-C
    public int StandardId { get; set; }
    public string EditionLabel { get; set; } = "";    // Rev C
    public string EffectiveDate { get; set; } = "";   // 2019-01-01
    public bool Deprecated { get; set; }
    public EfStandard Standard { get; set; } = null!;
    public List<EfMessageFamily> Families { get; set; } = [];
}

public class EfMessageFamily
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // MF-L16-J3
    public int EditionId { get; set; }
    public string FamilyDesignator { get; set; } = "";// J3, VMF-C2
    public string Description { get; set; } = "";
    public EfStandardEdition Edition { get; set; } = null!;
    public List<EfMessageType> Types { get; set; } = [];
}

public class EfMessageType
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // MT-J3-2
    public int FamilyId { get; set; }
    public string TypeDesignator { get; set; } = "";  // J3.2
    public string Description { get; set; } = "";     // Track Message
    public string Direction { get; set; } = "";       // TRANSMIT|RECEIVE|BIDIRECTIONAL
    public EfMessageFamily Family { get; set; } = null!;
    public List<EfMessageField> Fields { get; set; } = [];
}

public class EfMessageField
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // MFL-J3-2-001
    public int MessageTypeId { get; set; }
    public string FieldName { get; set; } = "";       // TRACK_NUMBER
    public string DataCategory { get; set; } = "";    // IDENTITY|KINEMATICS|EMITTER|LINK|STATUS
    public bool Mandatory { get; set; }
    public EfMessageType MessageType { get; set; } = null!;
}

public class EfProfile
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // PRF-L16-NAVAL-J3
    public string Name { get; set; } = "";
    public int MessageTypeId { get; set; }
    public string PlatformClass { get; set; } = "";   // SURFACE|AIR|SUBSURFACE
    public EfMessageType MessageType { get; set; } = null!;
}

// ── Lane B: System ─────────────────────────────────────────────────────────────

public class EfTranslatorComponent
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // TCOMP-001
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Language { get; set; } = "";        // C|Ada|C++
    public string Status { get; set; } = "ACTIVE";    // ACTIVE|DEPRECATED|UNDER_CHANGE
}

public class EfInterfaceContract
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // IC-001
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "";        // UDP/MULTICAST|MQ|REST
    public string Version { get; set; } = "";
}

public class EfSubsystem
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // SS-001
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";          // C2|INTEL|LOGISTICS|FIRE_CONTROL
}

public class EfPlatform
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // PLT-SHIP-001
    public string PlatformType { get; set; } = "";    // SURFACE|SUBSURFACE|AIR|LAND
    public bool Synthetic { get; set; } = true;
}

// ── Lane C: Assurance ──────────────────────────────────────────────────────────

public class EfRequirement
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // REQ-L16-001
    public string Statement { get; set; } = "";
    public string Type { get; set; } = "";            // FUNCTIONAL|INTEROPERABILITY|PERFORMANCE
    public string Priority { get; set; } = "";        // SHALL|SHOULD|MAY
    public string AllocatingStandard { get; set; } = "";
}

public class EfTestCase
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // TC-001
    public string Name { get; set; } = "";
    public string Method { get; set; } = "";          // SIMULATION|HARDWARE_IN_LOOP|ANALYSIS
    public string PassCriteria { get; set; } = "";
    public string Status { get; set; } = "NOT_RUN";   // PASSING|FAILING|BLOCKED|NOT_RUN
}

public class EfEvidenceArtifact
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // EA-001
    public string ArtifactType { get; set; } = "";    // TEST_LOG|ANALYSIS_REPORT|ATTESTATION
    public string DateProduced { get; set; } = "";
    public string Verdict { get; set; } = "";         // PASS|FAIL|INCONCLUSIVE
}

public class EfCertificationPackage
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // CP-001
    public string Name { get; set; } = "";
    public string CertificationAuthority { get; set; } = "";
    public string Status { get; set; } = "IN_PROGRESS";
}

public class EfMissionCapability
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // MCAP-001
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";        // SA|C2|FIRES|LOGISTICS
}

// ── Snapshot ────────────────────────────────────────────────────────────────────

public class EfBaseline
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // BL-Q1-2024
    public string Label { get; set; } = "";
    public string SnapshotDate { get; set; } = "";
    public bool Sealed { get; set; }
}

// ── Change Event ───────────────────────────────────────────────────────────────

public class EfChangeEvent
{
    public int Id { get; set; }
    public string ExtId { get; set; } = "";           // CHG-001
    public string Description { get; set; } = "";
    public string Severity { get; set; } = "";        // LOW|MODERATE|HIGH|CRITICAL
    public string AffectingStandard { get; set; } = "";
}

// ── Join tables ────────────────────────────────────────────────────────────────

public class EfBaselineEntry
{
    public int Id { get; set; }
    public int BaselineId { get; set; }
    public string EntityExtId { get; set; } = "";
    public string EntityType { get; set; } = "";      // Standard|TranslatorComponent|...
    public EfBaseline Baseline { get; set; } = null!;
}

public class EfComponentProfile
{
    public int Id { get; set; }
    public int ComponentId { get; set; }
    public int ProfileId { get; set; }
    public string ImplementationVersion { get; set; } = "";
    public EfTranslatorComponent Component { get; set; } = null!;
    public EfProfile Profile { get; set; } = null!;
}

public class EfComponentFieldUsage
{
    public int Id { get; set; }
    public int ComponentId { get; set; }
    public int FieldId { get; set; }
    public string Access { get; set; } = "READ";      // READ|WRITE
    public EfTranslatorComponent Component { get; set; } = null!;
    public EfMessageField Field { get; set; } = null!;
}

public class EfSubsystemComponent
{
    public int Id { get; set; }
    public int SubsystemId { get; set; }
    public int ComponentId { get; set; }
    public string DependencyType { get; set; } = "RUNTIME";
    public EfSubsystem Subsystem { get; set; } = null!;
    public EfTranslatorComponent Component { get; set; } = null!;
}

public class EfSubsystemPlatform
{
    public int Id { get; set; }
    public int SubsystemId { get; set; }
    public int PlatformId { get; set; }
    public string DeploymentDate { get; set; } = "";
    public EfSubsystem Subsystem { get; set; } = null!;
    public EfPlatform Platform { get; set; } = null!;
}

public class EfSubsystemCapability
{
    public int Id { get; set; }
    public int SubsystemId { get; set; }
    public int CapabilityId { get; set; }
    public EfSubsystem Subsystem { get; set; } = null!;
    public EfMissionCapability Capability { get; set; } = null!;
}

public class EfComponentRequirement
{
    public int Id { get; set; }
    public int ComponentId { get; set; }
    public int RequirementId { get; set; }
    public EfTranslatorComponent Component { get; set; } = null!;
    public EfRequirement Requirement { get; set; } = null!;
}

public class EfRequirementTest
{
    public int Id { get; set; }
    public int RequirementId { get; set; }
    public int TestCaseId { get; set; }
    public EfRequirement Requirement { get; set; } = null!;
    public EfTestCase TestCase { get; set; } = null!;
}

public class EfTestEvidence
{
    public int Id { get; set; }
    public int TestCaseId { get; set; }
    public int EvidenceId { get; set; }
    public string RunDate { get; set; } = "";
    public EfTestCase TestCase { get; set; } = null!;
    public EfEvidenceArtifact Evidence { get; set; } = null!;
}

public class EfEvidenceCertification
{
    public int Id { get; set; }
    public int EvidenceId { get; set; }
    public int PackageId { get; set; }
    public EfEvidenceArtifact Evidence { get; set; } = null!;
    public EfCertificationPackage Package { get; set; } = null!;
}

public class EfChangeEventTarget
{
    public int Id { get; set; }
    public int ChangeEventId { get; set; }
    public string TargetExtId { get; set; } = "";
    public string TargetType { get; set; } = "";      // MessageField|MessageType|TranslatorComponent
    public string Severity { get; set; } = "";
    public EfChangeEvent ChangeEvent { get; set; } = null!;
}

// ── DbContext ──────────────────────────────────────────────────────────────────

public class TacticalMessagingDbContext(DbContextOptions<TacticalMessagingDbContext> opts)
    : DbContext(opts)
{
    public DbSet<EfStandard> Standards => Set<EfStandard>();
    public DbSet<EfStandardEdition> Editions => Set<EfStandardEdition>();
    public DbSet<EfMessageFamily> Families => Set<EfMessageFamily>();
    public DbSet<EfMessageType> MessageTypes => Set<EfMessageType>();
    public DbSet<EfMessageField> MessageFields => Set<EfMessageField>();
    public DbSet<EfProfile> Profiles => Set<EfProfile>();
    public DbSet<EfTranslatorComponent> Components => Set<EfTranslatorComponent>();
    public DbSet<EfInterfaceContract> Contracts => Set<EfInterfaceContract>();
    public DbSet<EfSubsystem> Subsystems => Set<EfSubsystem>();
    public DbSet<EfPlatform> Platforms => Set<EfPlatform>();
    public DbSet<EfRequirement> Requirements => Set<EfRequirement>();
    public DbSet<EfTestCase> TestCases => Set<EfTestCase>();
    public DbSet<EfEvidenceArtifact> EvidenceArtifacts => Set<EfEvidenceArtifact>();
    public DbSet<EfCertificationPackage> CertificationPackages => Set<EfCertificationPackage>();
    public DbSet<EfMissionCapability> MissionCapabilities => Set<EfMissionCapability>();
    public DbSet<EfBaseline> Baselines => Set<EfBaseline>();
    public DbSet<EfChangeEvent> ChangeEvents => Set<EfChangeEvent>();
    public DbSet<EfBaselineEntry> BaselineEntries => Set<EfBaselineEntry>();
    public DbSet<EfComponentProfile> ComponentProfiles => Set<EfComponentProfile>();
    public DbSet<EfComponentFieldUsage> ComponentFieldUsages => Set<EfComponentFieldUsage>();
    public DbSet<EfSubsystemComponent> SubsystemComponents => Set<EfSubsystemComponent>();
    public DbSet<EfSubsystemPlatform> SubsystemPlatforms => Set<EfSubsystemPlatform>();
    public DbSet<EfSubsystemCapability> SubsystemCapabilities => Set<EfSubsystemCapability>();
    public DbSet<EfComponentRequirement> ComponentRequirements => Set<EfComponentRequirement>();
    public DbSet<EfRequirementTest> RequirementTests => Set<EfRequirementTest>();
    public DbSet<EfTestEvidence> TestEvidences => Set<EfTestEvidence>();
    public DbSet<EfEvidenceCertification> EvidenceCertifications => Set<EfEvidenceCertification>();
    public DbSet<EfChangeEventTarget> ChangeEventTargets => Set<EfChangeEventTarget>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<EfStandard>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfStandardEdition>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfMessageFamily>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfMessageType>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfMessageField>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfProfile>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfTranslatorComponent>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfInterfaceContract>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfSubsystem>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfPlatform>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfRequirement>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfTestCase>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfEvidenceArtifact>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfCertificationPackage>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfMissionCapability>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfBaseline>().HasIndex(e => e.ExtId).IsUnique();
        m.Entity<EfChangeEvent>().HasIndex(e => e.ExtId).IsUnique();
    }
}
