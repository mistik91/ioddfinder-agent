using System.Text.Json.Nodes;

namespace IoddFinder.Core;

public static class ViewerInspection
{
    public static JsonObject Select(JsonNode viewer, int vendorId, int deviceId, string revision,
        string? section = null, string? query = null, int offset = 0, int limit = 20)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("Offset must be nonnegative; limit must be 1 to 100.");
        var menus = viewer["menus"] as JsonArray ?? throw new InvalidDataException("Viewer response has no menus.");
        var result = new JsonObject
        {
            ["source"] = "live-viewer", ["vendorId"] = vendorId, ["deviceId"] = deviceId,
            ["ioLinkRevision"] = revision,
            ["releaseSelection"] = "Upstream viewer selects the release; use an exact IODD package for historical inspection."
        };
        if (string.IsNullOrWhiteSpace(section) && string.IsNullOrWhiteSpace(query))
        {
            result["sections"] = new JsonArray(menus.OfType<JsonObject>().Select(m => (JsonNode)new JsonObject
            {
                ["id"] = m["id"]?.DeepClone(), ["name"] = m["name"]?.DeepClone(),
                ["children"] = (m["children"] as JsonArray)?.Count ?? 0
            }).ToArray());
            return result;
        }
        IEnumerable<JsonNode> roots = menus.OfType<JsonNode>();
        if (!string.IsNullOrWhiteSpace(section))
        {
            roots = roots.Where(m => Equal(m["id"], section) || Equal(m["name"], section)).ToArray();
            if (!roots.Any()) throw new ArgumentException("Unknown section. Inspect without a section to list available names and IDs.");
        }
        var matches = roots.SelectMany(Walk).Where(n => n["type"]?.ToString() != "Menu")
            .Where(n => string.IsNullOrWhiteSpace(query) ||
                new[] { "id", "name", "description", "index" }.Any(k =>
                    n[k]?.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) == true)).ToArray();
        // Remove nested children here: each descendant is independently paginated.
        var items = matches.Skip(offset).Take(limit).Select(n =>
        {
            var copy = (JsonObject)n.DeepClone();
            copy.Remove("children");
            return (JsonNode)copy;
        }).ToArray();
        result["total"] = matches.Length;
        result["offset"] = offset;
        result["nextOffset"] = offset + items.Length < matches.Length ? offset + items.Length : null;
        result["items"] = new JsonArray(items);
        return result;
    }

    private static bool Equal(JsonNode? node, string text) => string.Equals(node?.ToString(), text, StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<JsonObject> Walk(JsonNode node)
    {
        if (node is not JsonObject obj) yield break;
        yield return obj;
        if (obj["children"] is JsonArray children)
            foreach (var child in children.OfType<JsonNode>())
                foreach (var descendant in Walk(child)) yield return descendant;
    }
}
