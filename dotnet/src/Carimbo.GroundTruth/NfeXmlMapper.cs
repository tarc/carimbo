using System.Xml.Linq;
using Carimbo.Domain;

namespace Carimbo.GroundTruth;

/// <summary>Raised when an NF-e XML lacks an element the documented mapping needs. The message names the element.</summary>
public sealed class NfeMappingException(string message) : Exception(message);

/// <summary>Stub, replaced in the GREEN commit.</summary>
public static class NfeXmlMapper
{
    public static Invoice Load(string path) => throw new NotImplementedException();

    public static Invoice Map(XDocument document) => throw new NotImplementedException();
}
