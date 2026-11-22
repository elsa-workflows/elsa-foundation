import "./elsa3Import.css";
import { StudioButton, TechnicalDetails, StudioShellBreadcrumbs, StudioPageHeader, StudioField } from "@elsa-workflows/studio-ui";
import {
  useEffect,
  useMemo,
  useRef,
  useState
} from "react";
import {
  CheckCircle2,
  FileArchive,
  Link2,
  LoaderCircle,
  Network,
  RefreshCw,
  ShieldCheck,
  Upload,
  XCircle
} from "lucide-react";
import {
  StudioHttpError,
  type StudioEndpointContext
} from "@elsa-workflows/studio-sdk";
import {
  analyzeElsa3ReusableCollection,
  applyElsa3ReusableImport,
  expandElsa3ReusableSelection,
  getElsa3ReusableImportReceipt,
  uploadElsa3ReusableCollection
} from "./api/elsa3ReusableImport";
import {
  appendElsa3ReusableImportAnalysis,
  groupElsa3ReusableImportItems,
  diagnosticPathSegmentKind,
  diagnosticSeverity,
  importPublicationStatus,
  importPublicationStatusLabel,
  importReceiptApplyMessage,
  importReceiptNeedsResume,
  importReceiptOutcomeLabel,
  importResourceDisposition,
  isElsa3ReusableImportItemSelectable,
  isPublishedImportStatus,
  publicationForSource,
  summarizeImportPublications,
  startElsa3ReusableImportAnalysis,
  summarizeElsa3ReusableImportSelection,
  type Elsa3ReusableImportAnalysis
} from "./elsa3ReusableImportModel";
import type {
  Elsa3MigrationDiagnostic,
  Elsa3ReusableImportItem,
  Elsa3ReusableImportPublication,
  Elsa3ReusableImportReceipt,
  Elsa3ReusableImportSelectionReadiness,
  Elsa3ReusableImportUploadResult
} from "./elsa3ReusableImportTypes";

const maximumClientUploadBytes = 16 * 1024 * 1024;
const analysisPageSize = 100;

type OperationStatus = "idle" | "loading" | "ready" | "failed";

export function Elsa3ReusableImportPage({
  context,
  navigate
}: {
  context: StudioEndpointContext;
  navigate(path: string): void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [collectionHandleInput, setCollectionHandleInput] = useState("");
  const [uploadStatus, setUploadStatus] = useState<OperationStatus>("idle");
  const [uploadResult, setUploadResult] = useState<Elsa3ReusableImportUploadResult | null>(null);
  const [analysisStatus, setAnalysisStatus] = useState<OperationStatus>("idle");
  const [analysis, setAnalysis] = useState<Elsa3ReusableImportAnalysis | null>(null);
  const [requestedSelection, setRequestedSelection] = useState<Set<string>>(new Set());
  const [expandedSelection, setExpandedSelection] = useState<Set<string>>(new Set());
  const [readiness, setReadiness] = useState<Elsa3ReusableImportSelectionReadiness | null>(null);
  const [selectionStatus, setSelectionStatus] = useState<OperationStatus>("idle");
  const [reviewOpen, setReviewOpen] = useState(false);
  const [idempotencyKey, setIdempotencyKey] = useState("");
  const [applyStatus, setApplyStatus] = useState<OperationStatus>("idle");
  const [receipt, setReceipt] = useState<Elsa3ReusableImportReceipt | null>(null);
  const [message, setMessage] = useState("");
  const [diagnostics, setDiagnostics] = useState<Elsa3MigrationDiagnostic[]>([]);
  const uploadController = useRef<AbortController | null>(null);
  const analysisController = useRef<AbortController | null>(null);
  const selectionController = useRef<AbortController | null>(null);
  const reviewedOperationGeneration = useRef(0);

  const effectiveCollectionHandle = uploadResult?.collectionHandle ?? collectionHandleInput.trim();
  const groups = useMemo(
    () => groupElsa3ReusableImportItems(analysis?.items ?? []),
    [analysis?.items]
  );
  const summary = useMemo(
    () => summarizeElsa3ReusableImportSelection(analysis?.items ?? [], expandedSelection),
    [analysis?.items, expandedSelection]
  );
  const selectedItems = useMemo(
    () => (analysis?.items ?? []).filter(item => expandedSelection.has(item.sourceVersionId)),
    [analysis?.items, expandedSelection]
  );
  const completeCycles = useMemo(
    () => uniqueCycles([
      ...(analysis?.diagnostics ?? []),
      ...(analysis?.items.flatMap(item => item.diagnostics) ?? [])
    ]),
    [analysis]
  );

  useEffect(() => () => {
    uploadController.current?.abort();
    analysisController.current?.abort();
    selectionController.current?.abort();
  }, []);

  const resetReviewedInput = (nextMessage = "") => {
    reviewedOperationGeneration.current += 1;
    analysisController.current?.abort();
    selectionController.current?.abort();
    setAnalysisStatus("idle");
    setAnalysis(null);
    setRequestedSelection(new Set());
    setExpandedSelection(new Set());
    setReadiness(null);
    setSelectionStatus("idle");
    setReviewOpen(false);
    setIdempotencyKey("");
    setApplyStatus("idle");
    setReceipt(null);
    setDiagnostics([]);
    setMessage(nextMessage);
  };

  const chooseFile = (nextFile: File | null) => {
    uploadController.current?.abort();
    setFile(nextFile);
    setUploadStatus("idle");
    setUploadResult(null);
    setCollectionHandleInput("");
    resetReviewedInput(nextFile ? "A new source file is staged. Upload it to create a new immutable collection handle." : "");
  };

  const changeCollectionHandle = (value: string) => {
    uploadController.current?.abort();
    setCollectionHandleInput(value);
    setFile(null);
    setUploadStatus("idle");
    setUploadResult(null);
    resetReviewedInput(value.trim() ? "The collection handle changed. Fresh analysis is required." : "");
  };

  const upload = async () => {
    if (!file) return;
    if (file.size > maximumClientUploadBytes) {
      setMessage(`The selected file is ${formatBytes(file.size)}. Studio limits a browser upload to ${formatBytes(maximumClientUploadBytes)} before the server applies its authoritative bound.`);
      setUploadStatus("failed");
      return;
    }
    const controller = new AbortController();
    uploadController.current?.abort();
    uploadController.current = controller;
    setUploadStatus("loading");
    setMessage(`Uploading ${formatBytes(file.size)} once. Apply will use only the immutable handle.`);
    try {
      const content = await file.text();
      if (controller.signal.aborted) return;
      const result = await uploadElsa3ReusableCollection(context, content, controller.signal);
      if (controller.signal.aborted) return;
      setUploadResult(result);
      setCollectionHandleInput("");
      setUploadStatus("ready");
      resetReviewedInput(`Immutable collection accepted: ${result.sourceVersionCount} exact source versions, expires ${formatDateTime(result.expiresAt)}.`);
    } catch (error) {
      if (isAbortError(error)) {
        setUploadStatus("idle");
        setMessage("Upload cancelled. No collection handle was accepted by Studio.");
        return;
      }
      setUploadStatus("failed");
      setMessage(importErrorMessage(error, "Studio could not upload this Elsa 3 collection."));
    }
  };

  const cancelUpload = () => {
    uploadController.current?.abort();
    uploadController.current = null;
    setUploadStatus("idle");
    setMessage("Upload cancelled. No collection handle was accepted by Studio.");
  };

  const analyze = async () => {
    if (!effectiveCollectionHandle) return;
    resetReviewedInput("Running preview. No Design documents are being written.");
    const controller = new AbortController();
    analysisController.current?.abort();
    analysisController.current = controller;
    setAnalysisStatus("loading");
    try {
      const page = await analyzeElsa3ReusableCollection(
        context,
        effectiveCollectionHandle,
        0,
        analysisPageSize,
        controller.signal
      );
      if (controller.signal.aborted) return;
      setAnalysis(startElsa3ReusableImportAnalysis(page));
      setAnalysisStatus("ready");
      setDiagnostics(page.diagnostics);
      setMessage(page.isComplete
        ? `Analysis complete for ${page.total} exact source versions.`
        : `Analyzed ${page.processed} of ${page.total} exact source versions. Load the next bounded page to continue.`);
    } catch (error) {
      if (isAbortError(error)) return;
      if (isPrivacyBoundaryError(error)) {
        redactAuthorizedState();
        return;
      }
      if (isExpiredCollectionError(error)) {
        expireCollection();
        return;
      }
      setAnalysisStatus("failed");
      setMessage(importErrorMessage(error, "Studio could not analyze this immutable collection."));
    }
  };

  const cancelAnalysis = () => {
    analysisController.current?.abort();
    analysisController.current = null;
    setAnalysisStatus(analysis ? "ready" : "idle");
    setMessage(analysis
      ? "The next analysis page was cancelled. Previously confirmed pages remain visible."
      : "Preview cancelled. No Design documents were written.");
  };

  const loadNextPage = async () => {
    if (!analysis || analysis.nextOffset === null || analysisStatus === "loading") return;
    const controller = new AbortController();
    analysisController.current?.abort();
    analysisController.current = controller;
    setAnalysisStatus("loading");
    try {
      const page = await analyzeElsa3ReusableCollection(
        context,
        analysis.collectionHandle,
        analysis.nextOffset,
        analysisPageSize,
        controller.signal
      );
      if (controller.signal.aborted) return;
      const next = appendElsa3ReusableImportAnalysis(analysis, page);
      setAnalysis(next);
      setAnalysisStatus("ready");
      setDiagnostics(next.diagnostics);
      setMessage(next.nextOffset === null
        ? `Analysis complete for ${next.total} exact source versions.`
        : `Analyzed ${next.processed} of ${next.total} exact source versions.`);
    } catch (error) {
      if (isAbortError(error)) return;
      if (isPrivacyBoundaryError(error)) {
        redactAuthorizedState();
        return;
      }
      if (isExpiredCollectionError(error)) {
        expireCollection();
        return;
      }
      setAnalysisStatus("failed");
      setMessage(importErrorMessage(error, "The next analysis page could not be loaded. The reviewed pages remain visible."));
    }
  };

  const changeSelection = async (item: Elsa3ReusableImportItem, checked: boolean) => {
    if (!analysis || !isElsa3ReusableImportItemSelectable(item)) return;
    const requested = new Set(requestedSelection);
    if (checked) requested.add(item.sourceVersionId);
    else requested.delete(item.sourceVersionId);
    setRequestedSelection(requested);
    setReviewOpen(false);
    setIdempotencyKey("");
    setReceipt(null);
    setApplyStatus("idle");
    if (requested.size === 0) {
      selectionController.current?.abort();
      setExpandedSelection(new Set());
      setReadiness(null);
      setSelectionStatus("idle");
      setMessage("Select at least one exact source version. Empty import selections are never expanded or applied.");
      return;
    }

    const controller = new AbortController();
    selectionController.current?.abort();
    selectionController.current = controller;
    setSelectionStatus("loading");
    setReadiness(null);
    setExpandedSelection(new Set());
    setDiagnostics([]);
    try {
      const result = await expandElsa3ReusableSelection(
        context,
        analysis.collectionHandle,
        analysis.planId,
        [...requested],
        controller.signal
      );
      if (controller.signal.aborted) return;
      setReadiness(result);
      setExpandedSelection(new Set(result.expandedSourceVersionIds));
      setSelectionStatus("ready");
      setDiagnostics(result.diagnostics);
      setMessage(result.addedDependencySourceVersionIds.length
        ? `${result.addedDependencySourceVersionIds.length} exact reusable dependency version(s) are required and selected by closure.`
        : result.isReady
          ? "The exact selection is dependency-closed and ready for final review."
          : "The selected closure is not ready. Review its structured diagnostics.");
    } catch (error) {
      if (isAbortError(error)) return;
      if (isPrivacyBoundaryError(error)) {
        redactAuthorizedState();
        return;
      }
      if (isExpiredCollectionError(error)) {
        expireCollection();
        return;
      }
      setSelectionStatus("failed");
      setReadiness(null);
      setExpandedSelection(new Set());
      setMessage(importErrorMessage(error, "Studio could not confirm the exact dependency closure."));
    }
  };

  const cancelSelection = () => {
    selectionController.current?.abort();
    selectionController.current = null;
    setSelectionStatus("idle");
    setReadiness(null);
    setExpandedSelection(new Set());
    setDiagnostics(analysis?.diagnostics ?? []);
    setMessage("Dependency-closure checking was cancelled. Change the requested selection to run a fresh authoritative check.");
  };

  const openReview = () => {
    if (!analysis || !readiness?.isReady || expandedSelection.size === 0 || applyStatus === "loading" || receipt) return;
    reviewedOperationGeneration.current += 1;
    setReviewOpen(true);
    setIdempotencyKey(createIdempotencyKey());
    setReceipt(null);
    setApplyStatus("idle");
    setMessage("Review binds the exact Plan ID, immutable collection handle, access scope, and source-version closure.");
  };

  const apply = async () => {
    if (!analysis || !reviewOpen || !idempotencyKey || expandedSelection.size === 0) return;
    const generation = reviewedOperationGeneration.current;
    setApplyStatus("loading");
    setMessage("Applying one authoritative atomic import. No partial Design result is accepted.");
    setDiagnostics([]);
    try {
      const result = await applyElsa3ReusableImport(
        context,
        analysis.collectionHandle,
        analysis.planId,
        [...expandedSelection].sort(),
        idempotencyKey
      );
      showReceipt(result, generation);
    } catch (error) {
      if (generation !== reviewedOperationGeneration.current) return;
      if (isPrivacyBoundaryError(error)) {
        redactAuthorizedState();
        return;
      }
      if (isExpiredCollectionError(error)) {
        expireCollection();
        return;
      }
      const rejection = atomicRejection(error);
      if (rejection) {
        setApplyStatus("failed");
        setDiagnostics(rejection.diagnostics);
        setMessage(rejection.message);
        return;
      }
      setMessage("The Apply response was ambiguous. Studio is reconciling through the durable receipt without resubmitting.");
      await reconcile(true, generation);
    }
  };

  const reconcile = async (
    retryIdenticalApplyIfMissing = true,
    generation = reviewedOperationGeneration.current
  ) => {
    if (!idempotencyKey || !analysis) return;
    setApplyStatus("loading");
    try {
      const result = await getElsa3ReusableImportReceipt(context, idempotencyKey);
      showReceipt(result, generation);
    } catch (error) {
      if (generation !== reviewedOperationGeneration.current) return;
      if (retryIdenticalApplyIfMissing && error instanceof StudioHttpError && error.status === 404) {
        try {
          const result = await applyElsa3ReusableImport(
            context,
            analysis.collectionHandle,
            analysis.planId,
            [...expandedSelection].sort(),
            idempotencyKey
          );
          showReceipt(result, generation);
          return;
        } catch (retryError) {
          if (generation !== reviewedOperationGeneration.current) return;
          if (isExpiredCollectionError(retryError)) {
            expireCollection();
            return;
          }
          if (isPrivacyBoundaryError(retryError)) {
            redactAuthorizedState();
            return;
          }
          const rejection = atomicRejection(retryError);
          setApplyStatus("failed");
          setDiagnostics(rejection?.diagnostics ?? []);
          setMessage(rejection?.message ?? identicalRetryReconciliationMessage(retryError));
          return;
        }
      }
      if (isPrivacyBoundaryError(error)) {
        redactAuthorizedState();
        return;
      }
      setApplyStatus("failed");
      setMessage(reconciliationMessage(error));
    }
  };

  const showReceipt = (
    result: Elsa3ReusableImportReceipt,
    generation = reviewedOperationGeneration.current
  ) => {
    if (generation !== reviewedOperationGeneration.current) return;
    setReceipt(result);
    setApplyStatus("ready");
    // The receipt owns unpublished-apply guidance so the sticky status bar cannot cover it or repeat it.
    setMessage(importReceiptNeedsResume(result) ? "" : importReceiptApplyMessage(result));
  };

  const redactAuthorizedState = () => {
    resetReviewedInput("The authorized Elsa 3 import resource could not be confirmed. Retained analysis and receipt details were cleared without disclosing a hidden collection or plan identity.");
    setFile(null);
    setCollectionHandleInput("");
    setUploadResult(null);
    setUploadStatus("idle");
  };

  const expireCollection = () => {
    resetReviewedInput("The immutable Elsa 3 collection expired. Upload the source again to create a fresh handle and run a new preview.");
    setCollectionHandleInput("");
    setUploadResult(null);
    setUploadStatus("idle");
  };

  return (
    <main
      className={message && !receipt ? "studio-page ad-page elsa3-import-pending" : "studio-page ad-page"}
      aria-labelledby="elsa3-import-title"
    >
      <StudioShellBreadcrumbs items={[{ label: "Activity definitions", disabled: applyStatus === "loading", onClick: () => navigate("/workflows/activity-definitions") }]} current="Import from Elsa 3" />
      <StudioPageHeader headingLevel={2} title={<>Import from Elsa 3</>} titleId="elsa3-import-title" description={<>
            Import reusable activities and the workflows that use them. Each workflow keeps a reference to the imported version.
          </>} actions={<><span className="elsa3-import-safety-badge"><ShieldCheck size={16} /> Uses reusable activities directly</span></>} />

      <section className="elsa3-import-callout" aria-label="Conversion contract">
        <strong>What gets imported</strong>
        <span>
          Preview the import before saving. Studio includes the activities your selection needs and saves the reviewed definitions together.
        </span>
      </section>

      <section className="elsa3-import-panel" aria-labelledby="elsa3-source-title">
        <header className="elsa3-import-split">
          <div>
            <h2 id="elsa3-source-title">1. Select an export</h2>
            <p>Upload an Elsa 3 JSON export or enter the ID of a collection you already uploaded.</p>
          </div>
          {uploadResult ? <span className="elsa3-import-success-badge"><CheckCircle2 size={14} /> Upload complete</span> : null}
        </header>
        <div className="elsa3-import-responsive-grid">
          <label className="elsa3-import-field">
            <span>Elsa 3 collection file</span>
            <input
              type="file"
              accept=".json,application/json"
              disabled={uploadStatus === "loading" || applyStatus === "loading"}
              onChange={event => chooseFile(event.target.files?.[0] ?? null)}
            />
            <small>{file ? `${file.name} · ${formatBytes(file.size)}` : `Maximum file size: ${formatBytes(maximumClientUploadBytes)}`}</small>
          </label>
          <span className="elsa3-import-or">or</span>
          <StudioField className="elsa3-import-field" label={<><span>Existing collection ID</span></>}><input
              value={collectionHandleInput}
              disabled={uploadStatus === "loading" || applyStatus === "loading"}
              onChange={event => changeCollectionHandle(event.target.value)}
              placeholder="Authorized collection handle"
            /><small>The handle is scope-bound and expires according to the server.</small></StudioField>
        </div>
        <div className="elsa3-import-actions">
          <StudioButton type="button" onClick={() => void upload()} disabled={!file || uploadStatus === "loading" || applyStatus === "loading"}>
            {uploadStatus === "loading" ? <LoaderCircle size={14} className="wf-spin" /> : <Upload size={14} />}
            {uploadStatus === "loading" ? "Uploading bounded collection" : "Upload once"}
          </StudioButton>
          {uploadStatus === "loading" ? <StudioButton type="button" onClick={cancelUpload}><XCircle size={14} /> Cancel upload</StudioButton> : null}
          <StudioButton
            type="button"
            variant="primary"
            disabled={!effectiveCollectionHandle || uploadStatus === "loading" || analysisStatus === "loading" || applyStatus === "loading"}
            onClick={() => void analyze()}
          >
            <FileArchive size={14} /> Analyze collection
          </StudioButton>
          {analysisStatus === "loading" ? <StudioButton type="button" onClick={cancelAnalysis}><XCircle size={14} /> Cancel analysis</StudioButton> : null}
        </div>
        {uploadResult ? (
          <dl className="elsa3-import-identity-grid">
            <div><dt>Collection handle</dt><dd><code>{uploadResult.collectionHandle}</code></dd></div>
            <div><dt>Exact source versions</dt><dd>{uploadResult.sourceVersionCount}</dd></div>
            <div><dt>Accepted bytes</dt><dd>{formatBytes(uploadResult.contentLength)}</dd></div>
            <div><dt>Expires</dt><dd>{formatDateTime(uploadResult.expiresAt)}</dd></div>
          </dl>
        ) : null}
      </section>

      {analysis ? (
        <section className="elsa3-import-panel" aria-labelledby="elsa3-analysis-title">
          <header className="elsa3-import-split">
            <div>
              <h2 id="elsa3-analysis-title">2. Review the analysis</h2>
              <p>One row per exact source version, grouped by Elsa 3 source definition.</p>
            </div>
            <span className="elsa3-import-progress">
              {analysis.processed} / {analysis.total} versions · {analysis.processedDiagnostics} / {analysis.totalDiagnostics} diagnostics
            </span>
          </header>
          <dl className="elsa3-import-identity-grid">
            <div><dt>Immutable handle</dt><dd><code>{analysis.collectionHandle}</code></dd></div>
            <div><dt>Plan ID</dt><dd><code>{analysis.planId}</code></dd></div>
          </dl>
          {completeCycles.length ? (
            <section className="elsa3-import-warning" aria-label="Complete dependency cycles">
              <h3>Complete dependency cycles</h3>
              {completeCycles.map(cycle => <code key={cycle.join(">")}>{cycle.join(" → ")} → {cycle[0]}</code>)}
              <p>Every invalid cycle member is unselectable. Valid unrelated closures remain available.</p>
            </section>
          ) : null}
          <div className="elsa3-import-group-stack">
            {groups.map(group => (
              <section key={group.sourceDefinitionId} className="elsa3-import-group">
                <header className="elsa3-import-group-header">
                  <strong>{group.sourceDefinitionId}</strong>
                  <span>{group.versions.length} exact version{group.versions.length === 1 ? "" : "s"}</span>
                </header>
                {group.versions.map(item => (
                  <SourceVersionRow
                    key={item.sourceVersionId}
                    item={item}
                    requested={requestedSelection.has(item.sourceVersionId)}
                    selected={expandedSelection.has(item.sourceVersionId)}
                    required={readiness?.addedDependencySourceVersionIds.includes(item.sourceVersionId) ?? false}
                    disabled={selectionStatus === "loading" || applyStatus === "loading"}
                    onChange={checked => void changeSelection(item, checked)}
                  />
                ))}
              </section>
            ))}
          </div>
          {analysis.nextOffset !== null ? (
            <StudioButton type="button" onClick={() => void loadNextPage()} disabled={analysisStatus === "loading"}>
              {analysisStatus === "loading" ? "Loading next bounded page…" : `Load next analysis page from ${analysis.nextOffset}`}
            </StudioButton>
          ) : <p className="elsa3-import-complete"><CheckCircle2 size={15} /> All {analysis.total} exact source versions are loaded.</p>}
          {selectionStatus === "loading" ? <StudioButton type="button" onClick={cancelSelection}><XCircle size={14} /> Cancel dependency check</StudioButton> : null}
        </section>
      ) : null}

      {analysis && expandedSelection.size > 0 ? (
        <section className="elsa3-import-panel" aria-labelledby="elsa3-selection-title">
          <header className="elsa3-import-split">
            <div>
              <h2 id="elsa3-selection-title">3. Review selected versions and dependencies</h2>
              <p>Each selected version includes the activities it depends on. Required versions appear below.</p>
            </div>
            <span className={readiness?.isReady ? "elsa3-import-success-badge" : "elsa3-import-warning-badge"}>
              {selectionStatus === "loading" ? "Checking dependencies" : readiness?.isReady ? "Ready" : "Not ready"}
            </span>
          </header>
          <div className="elsa3-import-selection-list">
            {selectedItems.map(item => (
              <span key={item.sourceVersionId}>
                <strong>{item.sourceDefinitionId} v{item.sourceVersion}</strong>
                <code>{item.sourceVersionId}</code>
                {readiness?.addedDependencySourceVersionIds.includes(item.sourceVersionId) ? <small>Required dependency</small> : <small>Requested owner</small>}
              </span>
            ))}
          </div>
          <StudioButton
            type="button"
            variant="primary"
            disabled={!readiness?.isReady || selectionStatus === "loading" || analysis.nextOffset !== null || applyStatus === "loading" || Boolean(receipt)}
            onClick={openReview}
          >
            Review exact import
          </StudioButton>
          {analysis.nextOffset !== null ? <p>Load the complete bounded analysis before final review.</p> : null}
        </section>
      ) : null}

      {reviewOpen && analysis ? (
        <section className="elsa3-import-panel" aria-labelledby="elsa3-review-title">
          <header className="elsa3-import-split">
            <div>
              <h2 id="elsa3-review-title">4. Confirm import</h2>
              <p>Review the selected versions before importing. If you change the selection, Studio runs the analysis again.</p>
            </div>
            <span className="elsa3-import-safety-badge"><ShieldCheck size={15} /> Saves all selected definitions together</span>
          </header>
          <dl className="elsa3-import-identity-grid">
            <div><dt>Plan ID</dt><dd><code>{analysis.planId}</code></dd></div>
            <div><dt>Collection handle</dt><dd><code>{analysis.collectionHandle}</code></dd></div>
            <div><dt>Request ID</dt><dd><code>{idempotencyKey}</code></dd></div>
            <div><dt>Access scope</dt><dd>Current authorized tenant and user</dd></div>
            <div><dt>Exact source versions</dt><dd>{summary.exactSourceVersions}</dd></div>
            <div><dt>Activity Definitions</dt><dd>{summary.activityDefinitions}</dd></div>
            <div><dt>Activity Definition versions</dt><dd>{summary.activityDefinitionVersions}</dd></div>
            <div><dt>Wrapper Workflows</dt><dd>{summary.wrapperWorkflows}</dd></div>
            <div><dt>Ordinary Workflows</dt><dd>{summary.ordinaryWorkflows}</dd></div>
            <div><dt>Exact reusable rewrites</dt><dd>{summary.rewrites}</dd></div>
          </dl>
          <div className="elsa3-import-actions">
            <StudioButton type="button" variant="primary" onClick={() => void apply()} disabled={applyStatus === "loading" || Boolean(receipt)}>
              {applyStatus === "loading" ? <LoaderCircle size={14} className="wf-spin" /> : <ShieldCheck size={14} />}
              {applyStatus === "loading" ? "Applying or reconciling" : "Apply reviewed import"}
            </StudioButton>
            {applyStatus === "failed" && !receipt ? (
              <StudioButton type="button" onClick={() => void reconcile(true)}><RefreshCw size={14} /> Check import status</StudioButton>
            ) : null}
          </div>
        </section>
      ) : null}

      {receipt ? <ImportReceipt receipt={receipt} navigate={navigate} /> : null}
      {diagnostics.length ? <DiagnosticList diagnostics={diagnostics} /> : null}
      {message ? (
        <div
          className={receipt ? "elsa3-import-status-static" : "elsa3-import-status"}
          role={applyStatus === "failed" || uploadStatus === "failed" || analysisStatus === "failed" || selectionStatus === "failed" ? "alert" : "status"}
          aria-live="polite"
        >
          {message}
        </div>
      ) : null}
    </main>
  );
}

function SourceVersionRow({
  item,
  requested,
  selected,
  required,
  disabled,
  onChange
}: {
  item: Elsa3ReusableImportItem;
  requested: boolean;
  selected: boolean;
  required: boolean;
  disabled: boolean;
  onChange(checked: boolean): void;
}) {
  const selectable = isElsa3ReusableImportItemSelectable(item);
  return (
    <article className="elsa3-import-row" data-selectable={selectable}>
      <label className="elsa3-import-row-choice">
        <input
          type="checkbox"
          checked={requested || selected}
          disabled={!selectable || disabled}
          onChange={event => onChange(event.target.checked)}
          aria-label={`Select ${item.sourceDefinitionId} exact source version ${item.sourceVersion}`}
        />
        <span>
          <strong>Source version {item.sourceVersion}</strong>
          <small>{item.isReusable ? "Reusable source → Activity Definition + wrapper Workflow" : "Ordinary consuming Workflow"}</small>
        </span>
      </label>
      <div className="elsa3-import-row-facts">
        <span><b>Generated identity</b>{item.isReusable ? item.activityTypeKey : item.workflowDefinitionId}</span>
        <span><b>Dependencies</b>{item.dependencies.length}</span>
        <span><b>Rewrites</b>{item.rewrites.length}</span>
        <span><b>Direct starts</b>{item.directStarts.length}</span>
        <span><b>Diagnostics</b>{item.diagnostics.length}</span>
      </div>
      {required ? <p className="elsa3-import-required">Required by the selected reusable dependency closure.</p> : null}
      {requested && !required ? <p className="elsa3-import-requested">Selected as an owner.</p> : null}
      {item.dependencies.length ? (
        <TechnicalDetails label="Exact dependencies and rewrites">
          <ul>
            {item.dependencies.map(dependency => (
              <li key={`${dependency.nodeId}:${dependency.targetSourceVersionId}`}>
                Node <code>{dependency.nodeId}</code> → source <code>{dependency.targetSourceVersionId}</code> → Activity Definition version <code>{dependency.targetActivityDefinitionVersionId}</code>
              </li>
            ))}
            {item.rewrites.map(rewrite => (
              <li key={`rewrite:${rewrite.nodeId}:${rewrite.targetSourceVersionId}`}>
                Rewrite <code>{rewrite.nodeId}</code> to exact target <code>{rewrite.targetActivityDefinitionVersionId}</code>
              </li>
            ))}
          </ul>
        </TechnicalDetails>
      ) : null}
      {item.directStarts.length ? (
        <TechnicalDetails label="Direct starts">
          <ul>{item.directStarts.map(start => <li key={`${start.nodeId}:${start.activityType}`}><code>{start.nodeId}</code> · {start.activityType}</li>)}</ul>
        </TechnicalDetails>
      ) : null}
      {item.diagnostics.length ? <DiagnosticList diagnostics={item.diagnostics} compact /> : null}
      <TechnicalDetails label="Deterministic technical IDs">
        <dl className="elsa3-import-technical-ids">
          <dt>Source version ID</dt><dd><code>{item.sourceVersionId}</code></dd>
          <dt>Workflow definition ID</dt><dd><code>{item.workflowDefinitionId}</code></dd>
          <dt>Workflow version ID</dt><dd><code>{item.workflowVersionId}</code></dd>
          {item.activityDefinitionId ? <><dt>Activity Definition ID</dt><dd><code>{item.activityDefinitionId}</code></dd></> : null}
          {item.activityDefinitionVersionId ? <><dt>Activity Definition version ID</dt><dd><code>{item.activityDefinitionVersionId}</code></dd></> : null}
          <dt>Source fingerprint</dt><dd><code>{item.sourceFingerprint}</code></dd>
        </dl>
      </TechnicalDetails>
    </article>
  );
}

function DiagnosticList({
  diagnostics,
  compact = false
}: {
  diagnostics: Elsa3MigrationDiagnostic[];
  compact?: boolean;
}) {
  return (
    <section className={compact ? "elsa3-import-compact-diagnostics" : "elsa3-import-diagnostics"} aria-label="Structured Elsa 3 import diagnostics">
      {!compact ? <h2>Structured diagnostics</h2> : null}
      {diagnostics.map((diagnostic, index) => (
        <article key={`${diagnostic.code}:${diagnostic.path}:${index}`} className="elsa3-import-diagnostic">
          <header className="elsa3-import-split">
            <strong>{diagnostic.code}</strong>
            <span>{diagnosticSeverity(diagnostic.severity)}</span>
          </header>
          <p>{diagnostic.message}</p>
          {diagnostic.pathSegments.length ? (
            <ol className="elsa3-import-path">
              {diagnostic.pathSegments.map((segment, segmentIndex) => (
                <li key={`${segment.kind}:${segment.identity}:${segmentIndex}`}>
                  <b>{diagnosticPathSegmentKind(segment.kind)}</b> <code>{segment.identity}</code>{segment.location ? ` · ${segment.location}` : ""}
                </li>
              ))}
            </ol>
          ) : diagnostic.path ? <code>{diagnostic.path}</code> : null}
          {diagnostic.cycle.length ? <p><b>Cycle:</b> {diagnostic.cycle.join(" → ")} → {diagnostic.cycle[0]}</p> : null}
          {diagnostic.guidance ? <p><b>Guidance:</b> {diagnostic.guidance}</p> : null}
        </article>
      ))}
    </section>
  );
}

function ImportReceipt({
  receipt,
  navigate
}: {
  receipt: Elsa3ReusableImportReceipt;
  navigate(path: string): void;
}) {
  const publicationSummary = summarizeImportPublications(receipt.publications);
  const outcomeLabel = importReceiptOutcomeLabel(receipt);
  const unpublished = publicationSummary.outcome === "partlyPublished" || publicationSummary.outcome === "nonePublished";
  return (
    <section
      className={unpublished ? "elsa3-import-partial-receipt" : "elsa3-import-receipt"}
      aria-labelledby="elsa3-receipt-title"
    >
      <header className="elsa3-import-receipt-header">
        <h2 id="elsa3-receipt-title" className="elsa3-import-receipt-title">Import results</h2>
        <span className={unpublished ? "elsa3-import-warning-badge" : "elsa3-import-success-badge"} data-receipt-outcome={outcomeLabel}>
          {unpublished ? <XCircle size={14} /> : <CheckCircle2 size={14} />} {outcomeLabel}
        </span>
        <code>{receipt.receiptId}</code>
      </header>
      {unpublished ? (
        <p className="elsa3-import-resume" role="status">
          {importReceiptApplyMessage(receipt)}
        </p>
      ) : null}
      <dl className="elsa3-import-identity-grid">
        <div><dt>Plan ID</dt><dd><code>{receipt.planId}</code></dd></div>
        <div><dt>Collection handle</dt><dd><code>{receipt.collectionHandle}</code></dd></div>
        <div><dt>Selection fingerprint</dt><dd><code>{receipt.selectionFingerprint}</code></dd></div>
        <div><dt>Completed</dt><dd>{formatDateTime(receipt.completedAt)}</dd></div>
      </dl>
      <div className="elsa3-import-receipt-sources">
        {receipt.sources.map(source => {
          const publication = publicationForSource(receipt.publications, source.sourceVersionId);
          return (
            <article key={source.sourceVersionId} className="elsa3-import-row">
              <header className="elsa3-import-split">
                <strong>{source.sourceDefinitionId} · {source.sourceVersionId}</strong>
                <span>{importResourceDisposition(source.workflowDisposition)}</span>
              </header>
              {publication ? <PublicationState publication={publication} /> : null}
              <div className="elsa3-import-actions">
                <StudioButton
                  type="button"
                  onClick={() => navigate(`/workflows/definitions?definition=${encodeURIComponent(source.workflowDefinitionId)}&version=${encodeURIComponent(source.workflowVersionId)}`)}
                >
                  <Network size={14} /> Open {source.activityDefinitionId ? "wrapper " : ""}Workflow
                </StudioButton>
                {source.activityDefinitionId && source.activityDefinitionVersionId ? (
                  <StudioButton
                    type="button"
                    onClick={() => navigate(`/workflows/activity-definitions?definition=${encodeURIComponent(source.activityDefinitionId!)}&section=versions&version=${encodeURIComponent(source.activityDefinitionVersionId!)}`)}
                  >
                    <Link2 size={14} /> Open Activity Definition version
                  </StudioButton>
                ) : null}
              </div>
              <small>
                Workflow: {importResourceDisposition(source.workflowDisposition)}
                {source.activityDefinitionDisposition !== null && source.activityDefinitionDisposition !== undefined
                  ? ` · Activity Definition: ${importResourceDisposition(source.activityDefinitionDisposition)}`
                  : ""}
                {source.activityVersionDisposition !== null && source.activityVersionDisposition !== undefined
                  ? ` · Activity Definition version: ${importResourceDisposition(source.activityVersionDisposition)}`
                  : ""}
                <br />
                Workflow navigation identity: {source.workflowNavigationIdentity}
                {source.activityVersionNavigationIdentity ? ` · Activity version navigation identity: ${source.activityVersionNavigationIdentity}` : ""}
              </small>
            </article>
          );
        })}
      </div>
      {receipt.warnings?.length ? <ConversionWarnings warnings={receipt.warnings} /> : null}
    </section>
  );
}

function PublicationState({ publication }: { publication: Elsa3ReusableImportPublication }) {
  const status = importPublicationStatus(publication.status);
  const published = isPublishedImportStatus(publication.status);
  return (
    <section
      className={published ? "elsa3-import-publication" : "elsa3-import-unpublished-publication"}
      aria-label={`Publication state for ${publication.sourceVersionId}`}
      data-status={status}
    >
      <header className="elsa3-import-split">
        <strong>{importPublicationStatusLabel(publication.status)}</strong>
        {publication.errorCode ? <code>{publication.errorCode}</code> : null}
      </header>
      {publication.blockedBy ? <p>Blocked by <code>{publication.blockedBy}</code></p> : null}
      {(publication.diagnostics ?? []).map((diagnostic, index) => (
        <article key={`${diagnostic.code ?? diagnostic.message}:${index}`}>
          {diagnostic.message ? <p>{diagnostic.message}</p> : null}
          {diagnostic.remediation ? <p><b>Remediation:</b> {diagnostic.remediation}</p> : null}
        </article>
      ))}
    </section>
  );
}

function ConversionWarnings({ warnings }: { warnings: Elsa3MigrationDiagnostic[] }) {
  return (
    <section className="elsa3-import-warning" aria-label="Conversion warnings">
      <h3>Conversion warnings</h3>
      <p>These describe where Elsa 4 behaves differently at run time. They stay on the receipt.</p>
      {warnings.map((warning, index) => (
        <article key={`${warning.code}:${warning.path}:${index}`} className="elsa3-import-diagnostic">
          <header className="elsa3-import-split">
            <strong>{warning.code}</strong>
            <span>{diagnosticSeverity(warning.severity)}</span>
          </header>
          <p>{warning.message}</p>
          {warning.guidance ? <p><b>Guidance:</b> {warning.guidance}</p> : null}
        </article>
      ))}
    </section>
  );
}

function atomicRejection(error: unknown) {
  if (!(error instanceof StudioHttpError)) return null;
  const payload = error.payload as { errorCode?: string; diagnostics?: Elsa3MigrationDiagnostic[]; detail?: string } | null;
  if (error.status !== 409 && error.status !== 422) return null;
  const guaranteed = payload?.errorCode === "elsa3.import.identity-collision" ||
    payload?.errorCode === "elsa3.import.validation-failed" ||
    payload?.errorCode === "elsa3.import.idempotency-conflict";
  if (!guaranteed) return null;
  return {
    diagnostics: payload?.diagnostics ?? [],
    message: `${payload?.detail ?? error.message} The authoritative atomic operation guarantees that no partial Design documents were written.`
  };
}

function reconciliationMessage(error: unknown) {
  if (error instanceof StudioHttpError && error.status === 404) {
    return "No durable receipt is visible yet. The outcome remains unconfirmed; Apply has not been resubmitted. Retry receipt reconciliation.";
  }
  if (error instanceof StudioHttpError && (error.status === 401 || error.status === 403)) {
    return "Receipt reconciliation is not authorized in this access scope. No hidden import identity was disclosed and Apply was not resubmitted.";
  }
  return "Receipt reconciliation is temporarily unavailable. The outcome remains unconfirmed and Apply was not resubmitted.";
}

function identicalRetryReconciliationMessage(error: unknown) {
  if (error instanceof StudioHttpError && error.status === 404) {
    return "The identical idempotent Apply retry found no visible collection or receipt. Retained import details were not accepted; start again from an authorized collection.";
  }
  return "The identical idempotent Apply retry did not return a confirmed outcome. Reconcile the durable receipt with the same request ID; never start a second import.";
}

function importErrorMessage(error: unknown, fallback: string) {
  if (!(error instanceof StudioHttpError)) return fallback;
  const payload = error.payload as { detail?: string } | null;
  if (error.status === 401 || error.status === 403 || error.status === 404) {
    return "The authorized Elsa 3 import resource could not be confirmed. No hidden collection or plan identity was disclosed.";
  }
  return payload?.detail || error.message || fallback;
}

function isPrivacyBoundaryError(error: unknown) {
  return error instanceof StudioHttpError &&
    (error.status === 401 || error.status === 403 || error.status === 404);
}

function isExpiredCollectionError(error: unknown) {
  return error instanceof StudioHttpError && error.status === 410;
}

function uniqueCycles(diagnostics: Elsa3MigrationDiagnostic[]) {
  const cycles = new Map<string, string[]>();
  diagnostics.forEach(diagnostic => {
    if (diagnostic.cycle.length) cycles.set(diagnostic.cycle.join(">"), diagnostic.cycle);
  });
  return [...cycles.values()];
}

function createIdempotencyKey() {
  return globalThis.crypto?.randomUUID?.() ?? `elsa3-import-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === "AbortError";
}

function formatBytes(value: number) {
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

function formatDateTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
