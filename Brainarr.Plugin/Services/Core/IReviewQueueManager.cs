using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Interface for managing review queue operations.
    /// </summary>
    public interface IReviewQueueManager
    {
        /// <summary>
        /// Handles review status updates (accept/reject).
        /// </summary>
        object HandleReviewUpdate(string artist, string album, ReviewQueueService.ReviewStatus status, string notes = null);

        /// <summary>
        /// Handles marking an item as "never again".
        /// </summary>
        object HandleReviewNever(string artist, string album, string notes = null);

        /// <summary>
        /// Applies approvals from selected keys.
        /// </summary>
        object ApplyApprovalsNow(BrainarrSettings settings, string keysCsv = null);

        /// <summary>
        /// Clears approval selections from settings.
        /// </summary>
        object ClearApprovalSelections(BrainarrSettings settings);

        /// <summary>
        /// Batch rejects or marks items as "never".
        /// </summary>
        object RejectOrNeverSelected(BrainarrSettings settings, string keysCsv, ReviewQueueService.ReviewStatus status);

        /// <summary>
        /// Gets pending review queue items as UI options.
        /// </summary>
        object GetReviewOptions();

        /// <summary>
        /// Gets review summary statistics.
        /// </summary>
        object GetReviewSummaryOptions();

        /// <summary>
        /// Gets the pending review queue items.
        /// </summary>
        object GetPendingItems();

        /// <summary>
        /// Gets review queue counts.
        /// </summary>
        (int pending, int accepted, int rejected, int never) GetCounts();

        /// <summary>
        /// Dequeues accepted items from the review queue.
        /// </summary>
        List<Recommendation> DequeueAccepted();

        /// <summary>
        /// Sets the status of a review queue item.
        /// </summary>
        bool SetStatus(string artist, string album, ReviewQueueService.ReviewStatus status, string notes = null);
    }
}
