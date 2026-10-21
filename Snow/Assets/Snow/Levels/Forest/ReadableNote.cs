using Unity.Collections;
using Unity.Netcode;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// A paper item that can be read (FOREST_U3 F4.3): the note id from Forest_Notes.json, set by the server before the
    /// item spawns (the placement's readable). Empty: the same model as plain dressing, not readable.
    /// </summary>
    public class ReadableNote : NetworkBehaviour
    {
        readonly NetworkVariable<FixedString32Bytes> _noteId = new NetworkVariable<FixedString32Bytes>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public string NoteId => _noteId.Value.ToString();

        /// <summary>Server, before Spawn.</summary>
        public void SetNote(string id) => _noteId.Value = new FixedString32Bytes(id ?? string.Empty);
    }
}
