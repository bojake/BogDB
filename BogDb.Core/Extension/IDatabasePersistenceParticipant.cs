namespace BogDb.Core.Extension;

/// <summary>
/// Optional extension-owned persistence hook invoked during database checkpointing.
/// Implementations must persist only extension-managed state and should do so
/// atomically when writing durable files.
/// </summary>
public interface IDatabasePersistenceParticipant
{
    void Persist(Main.BogDatabase database);
}
