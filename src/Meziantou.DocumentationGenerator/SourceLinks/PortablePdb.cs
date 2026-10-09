using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Meziantou.DocumentationGenerator.SourceLinks;

/// <summary>Reads document locations and the Source Link map of a portable PDB.</summary>
internal sealed class PortablePdb : IDisposable
{
    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    private readonly MetadataReaderProvider _provider;
    private readonly MetadataReader _reader;
    private readonly Dictionary<int, MethodDefinitionHandle> _moveNextByKickoff = [];

    private PortablePdb(MetadataReaderProvider provider)
    {
        _provider = provider;
        _reader = provider.GetMetadataReader();

        // Async and iterator methods have their body in the MoveNext method of a state machine
        foreach (var handle in _reader.MethodDebugInformation)
        {
            var definition = handle.ToDefinitionHandle();
            var kickoff = _reader.GetMethodDebugInformation(handle).GetStateMachineKickoffMethod();
            if (!kickoff.IsNil)
            {
                _moveNextByKickoff[MetadataTokens.GetRowNumber(kickoff)] = definition;
            }
        }
    }

    /// <summary>PDB identifier: the GUID and timestamp that must match the CodeView entry of the assembly.</summary>
    public ImmutableArray<byte> Id => _reader.DebugMetadataHeader?.Id ?? [];

    public static PortablePdb? TryCreate(MetadataReaderProvider provider)
    {
        try
        {
            var reader = provider.GetMetadataReader();
            return reader.DebugMetadataHeader is null ? null : new PortablePdb(provider);
        }
        catch (BadImageFormatException)
        {
            provider.Dispose();
            return null;
        }
    }

    public static PortablePdb? TryCreate(byte[] bytes) => TryCreate(MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(bytes)));

    public bool Matches(Guid guid, uint stamp)
    {
        var id = Id;
        if (id.Length != 20)
            return false;

        return new Guid(id.AsSpan(0, 16)) == guid && BitConverter.ToUInt32(id.AsSpan(16, 4)) == stamp;
    }

    public SourceLinkMap? GetSourceLinkMap()
    {
        foreach (var handle in _reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var information = _reader.GetCustomDebugInformation(handle);
            if (_reader.GetGuid(information.Kind) == SourceLinkKind)
                return SourceLinkMap.TryParse(_reader.GetBlobBytes(information.Value));
        }

        return null;
    }

    /// <summary>Returns the document and line of the first visible sequence point of a method, or <see langword="null"/>.</summary>
    public (string Document, int Line)? GetMethodLocation(int methodToken)
    {
        if (MetadataTokens.EntityHandle(methodToken) is not { Kind: HandleKind.MethodDefinition } handle)
            return null;

        var row = MetadataTokens.GetRowNumber(handle);
        if (row <= 0 || row > _reader.MethodDebugInformation.Count)
            return null;

        var method = _moveNextByKickoff.TryGetValue(row, out var moveNext) ? moveNext : MetadataTokens.MethodDefinitionHandle(row);
        var information = _reader.GetMethodDebugInformation(method);
        foreach (var point in information.GetSequencePoints())
        {
            if (point.IsHidden)
                continue;

            var document = _reader.GetDocument(point.Document);
            return (_reader.GetString(document.Name), point.StartLine);
        }

        return null;
    }

    public void Dispose() => _provider.Dispose();
}
