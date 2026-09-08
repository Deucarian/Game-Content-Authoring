using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentEditSessionCoordinator : IDisposable
    {
        private static GameContentEditSessionCoordinator shared;
        private int _attachedViews;
        private readonly GameContentEditSessionRegistry _sessions = new GameContentEditSessionRegistry();
        private readonly GameContentReferenceQueries _references;
        private readonly GameContentEditValidation _validation;
        private readonly GameContentFieldEditOperations _fields;
        private readonly GameContentEditTransaction _transaction;
        private readonly GameContentEditSessionOpening _opening;

        public GameContentEditSessionCoordinator()
        {
            _references = new GameContentReferenceQueries(_sessions);
            _validation = new GameContentEditValidation(_sessions);
            _fields = new GameContentFieldEditOperations(_sessions, _validation);
            _transaction = new GameContentEditTransaction(_sessions, _validation, NotifyRefresh);
            _opening = new GameContentEditSessionOpening(_sessions);
        }

        public static GameContentEditSessionCoordinator Shared
        {
            get
            {
                if (shared == null || shared._sessions.IsDisposed) shared = new GameContentEditSessionCoordinator();
                return shared;
            }
        }

        public event Action RefreshRequested;
        internal IDisposable AttachView(Action refresh)
        {
            if (_sessions.IsDisposed) throw new ObjectDisposedException(nameof(GameContentEditSessionCoordinator));
            _attachedViews++;
            RefreshRequested += refresh;
            return new ViewLease(this, refresh);
        }

        private sealed class ViewLease : IDisposable
        {
            private GameContentEditSessionCoordinator owner;
            private readonly Action refresh;
            internal ViewLease(GameContentEditSessionCoordinator owner, Action refresh)
            { this.owner = owner; this.refresh = refresh; }
            public void Dispose()
            {
                GameContentEditSessionCoordinator current = owner;
                if (current == null) return;
                owner = null;
                current.RefreshRequested -= refresh;
                current._attachedViews--;
                if (current._attachedViews == 0 && !current._sessions.IsDisposed) current.Reset();
            }
        }

        public int ActiveSourceCount => _sessions.ActiveSourceCount;
        public int TrackedSessionCount => _sessions.TrackedSessionCount;

        public GameContentReferenceCandidateSet GetReferenceCandidates(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentCollectionItemKey replacingItemKey = null)
            => _references.GetReferenceCandidates(active, fieldId, replacingItemKey);

        public GameContentReferenceEvaluation EvaluateReferenceTarget(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentRecordKey targetKey)
            => _references.EvaluateReferenceTarget(active, fieldId, targetKey);

        public GameContentReferenceCandidateSet GetStructuredReferenceCandidates(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentStructuredRowKey rowKey,
            string rowFieldId)
            => _references.GetStructuredReferenceCandidates(active, fieldId, rowKey, rowFieldId);

        public GameContentReferenceEvaluation EvaluateStructuredRowReference(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentStructuredRowKey rowKey,
            string rowFieldId,
            GameContentRecordKey targetKey)
            => _references.EvaluateStructuredRowReference(active, fieldId, rowKey, rowFieldId, targetKey);

        public GameContentReferenceChangeReview GetReferenceChangeReview(
            GameContentActiveEditSession active,
            GameContentProposedChange change)
            => _references.GetReferenceChangeReview(active, change);

        public GameContentCollectionChangeReview GetCollectionChangeReview(
            GameContentActiveEditSession active,
            GameContentProposedChange change)
            => _references.GetCollectionChangeReview(active, change);

        public GameContentStructuredCollectionChangeReview GetStructuredCollectionChangeReview(
            GameContentActiveEditSession active,
            GameContentProposedChange change)
            => _references.GetStructuredCollectionChangeReview(active, change);

        public GameContentValidationPreview Preview(GameContentActiveEditSession active)
            => _validation.Preview(active);

        public GameContentStaleCheckResult CheckStale(GameContentActiveEditSession active)
            => _validation.CheckStale(active);

        public GameContentEditOperationResult Apply(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentFieldValue value)
            => _fields.Apply(active, fieldId, value);

        public GameContentEditOperationResult ValidateCollectionOperation(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentCollectionOperation operation)
            => _fields.ValidateCollectionOperation(active, fieldId, operation);

        public GameContentEditOperationResult ApplyCollectionOperation(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentCollectionOperation operation)
            => _fields.ApplyCollectionOperation(active, fieldId, operation);

        public GameContentEditOperationResult RestoreOriginalCollectionOrder(
            GameContentActiveEditSession active,
            string fieldId)
            => _fields.RestoreOriginalCollectionOrder(active, fieldId);

        public GameContentStructuredCollectionOperationResult ValidateStructuredOperation(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentStructuredCollectionOperation operation)
            => _fields.ValidateStructuredOperation(active, fieldId, operation);

        public GameContentStructuredCollectionOperationResult ApplyStructuredOperation(
            GameContentActiveEditSession active,
            string fieldId,
            GameContentStructuredCollectionOperation operation)
            => _fields.ApplyStructuredOperation(active, fieldId, operation);

        public GameContentStructuredCollectionOperationResult RestoreOriginalStructuredOrder(
            GameContentActiveEditSession active,
            string fieldId)
            => _fields.RestoreOriginalStructuredOrder(active, fieldId);

        public GameContentEditOperationResult Undo(GameContentActiveEditSession active)
            => _fields.Undo(active);

        public GameContentEditOperationResult Redo(GameContentActiveEditSession active)
            => _fields.Redo(active);

        public GameContentCommitResult Commit(GameContentActiveEditSession active, bool confirmWarnings)
            => _transaction.Commit(active, confirmWarnings);

        public GameContentRollbackResult Rollback(GameContentActiveEditSession active)
            => _transaction.Rollback(active);

        public GameContentEditAvailability GetAvailability(
            GameContentPackContext context,
            GameContentRecordDescriptor record,
            string lensId = null)
            => _opening.GetAvailability(context, record, lensId);

        public GameContentEditBeginResult BeginEdit(
            GameContentPackContext context,
            GameContentRecordDescriptor record,
            string lensId = null)
            => _opening.BeginEdit(context, record, lensId);

        public bool TryGetSession(GameContentRecordKey key, out GameContentActiveEditSession session)
            => _sessions.TryGetSession(key, out session);

        public GameContentRollbackResult Cancel(GameContentActiveEditSession active)
            => _transaction.Rollback(active);

        public bool Dismiss(GameContentActiveEditSession active)
        {
            if (!_sessions.Owns(active) || !active.IsTerminal) return false;
            _sessions.Remove(active, true);
            return true;
        }

        public void Reconcile(GameContentPackCatalog catalog) => _sessions.Reconcile(catalog);
        public void Reset() => _sessions.Reset();

        internal static void ResetSharedForTests()
        {
            shared?.Dispose();
            shared = null;
        }

        public void Dispose()
        {
            _sessions.Dispose();
            RefreshRequested = null;
        }

        private void NotifyRefresh(GameContentActiveEditSession active)
        {
            Action handlers = RefreshRequested;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception exception)
                {
                    active.Message = "The source transaction completed, but a view could not refresh. " +
                                     "Refresh the library before continuing. " + exception.GetBaseException().Message;
                }
            }
        }
    }
}
