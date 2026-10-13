using JoelRichPodcast.Functions.Models;

namespace JoelRichPodcast.Backfill;

public static class BackfillPlanner
{
    public static IReadOnlyList<BackfillPlanItem> Plan(
        IReadOnlyList<BackfillEpisode> candidates,
        IReadOnlyList<EpisodeEntity> existingEpisodes)
    {
        var duplicateSources = candidates
            .GroupBy(candidate => candidate.SourceUrl, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicateKeys = candidates
            .GroupBy(candidate => candidate.RowKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        var existingBySource = existingEpisodes
            .Where(episode => !string.IsNullOrWhiteSpace(episode.SourceUrl))
            .ToLookup(episode => episode.SourceUrl!, StringComparer.OrdinalIgnoreCase);
        var existingByKey = existingEpisodes.ToLookup(
            episode => (episode.PartitionKey, episode.RowKey));

        return candidates.Select(candidate =>
        {
            if (duplicateSources.Contains(candidate.SourceUrl))
                return BackfillPlanItem.CreateConflict(candidate, "Manifest contains the source URL more than once.");
            if (duplicateKeys.Contains(candidate.RowKey))
                return BackfillPlanItem.CreateConflict(candidate, "Manifest contains the row key more than once.");

            var sourceMatches = existingBySource[candidate.SourceUrl].ToList();
            if (sourceMatches.Count > 1)
                return BackfillPlanItem.CreateConflict(candidate, "Multiple stored episodes have the same source URL.");
            if (sourceMatches.Count == 1)
                return BackfillPlanItem.Existing(
                    candidate,
                    sourceMatches[0],
                    DescribeDifferences(candidate, sourceMatches[0]));

            var keyMatches = existingByKey[("roundup", candidate.RowKey)].ToList();
            return keyMatches.Count switch
            {
                0 => BackfillPlanItem.Add(candidate),
                1 when string.Equals(
                    keyMatches[0].SourceUrl,
                    candidate.SourceUrl,
                    StringComparison.OrdinalIgnoreCase) => BackfillPlanItem.Existing(candidate, keyMatches[0]),
                _ => BackfillPlanItem.CreateConflict(candidate, "The proposed row key is already used by another source.")
            };
        }).ToList();
    }

    private static string? DescribeDifferences(BackfillEpisode candidate, EpisodeEntity existing)
    {
        var differences = new List<string>();
        AddDifference(differences, nameof(EpisodeEntity.RowKey), candidate.RowKey, existing.RowKey);
        AddDifference(differences, nameof(EpisodeEntity.AudioUrl), candidate.AudioUrl, existing.AudioUrl);
        AddDifference(differences, nameof(EpisodeEntity.Title), candidate.Title, existing.Title);
        AddDifference(differences, nameof(EpisodeEntity.Description), candidate.Description, existing.Description);
        AddDifference(differences, nameof(EpisodeEntity.PublishDate), candidate.PublishDate, existing.PublishDate);
        AddDifference(differences, nameof(EpisodeEntity.RoundupUrl), candidate.RoundupUrl, existing.RoundupUrl);
        AddDifference(differences, nameof(EpisodeEntity.RoundupIndex), candidate.RoundupIndex, existing.RoundupIndex);
        return differences.Count == 0 ? null : $"Stored metadata differs: {string.Join(", ", differences)}.";
    }

    private static void AddDifference<T>(List<string> differences, string name, T candidate, T existing)
    {
        if (!EqualityComparer<T>.Default.Equals(candidate, existing))
            differences.Add(name);
    }
}

public sealed record BackfillPlanItem(
    BackfillDisposition Disposition,
    BackfillEpisode Candidate,
    EpisodeEntity? ExistingEpisode,
    string? Detail)
{
    public static BackfillPlanItem Add(BackfillEpisode candidate) =>
        new(BackfillDisposition.Add, candidate, null, null);

    public static BackfillPlanItem Existing(
        BackfillEpisode candidate,
        EpisodeEntity existingEpisode,
        string? detail = null) =>
        new(BackfillDisposition.AlreadyExists, candidate, existingEpisode, detail);

    public static BackfillPlanItem CreateConflict(BackfillEpisode candidate, string conflict) =>
        new(BackfillDisposition.Conflict, candidate, null, conflict);
}

public enum BackfillDisposition
{
    Add,
    AlreadyExists,
    Conflict
}
