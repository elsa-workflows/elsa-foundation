import { ChevronLeft, ChevronRight } from "lucide-react";
import { definitionPageSizes } from "./constants";
import { getTotalPages } from "./editorHelpers";

export function DefinitionPager({ page, pageSize, totalCount, itemCount, hasNextPage, onPageChange, onPageSizeChange }: {
  page: number;
  pageSize: number;
  totalCount?: number;
  itemCount: number;
  hasNextPage?: boolean;
  onPageChange(page: number): void;
  onPageSizeChange(pageSize: number): void;
}) {
  const hasKnownTotal = typeof totalCount === "number";
  const totalPages = hasKnownTotal ? getTotalPages(totalCount, pageSize) : null;
  const isEmptyPage = itemCount === 0;
  const firstItem = isEmptyPage || (hasKnownTotal && totalCount === 0) ? 0 : ((page - 1) * pageSize) + 1;
  const lastItem = isEmptyPage ? 0 : hasKnownTotal ? Math.min(page * pageSize, totalCount) : firstItem + itemCount - 1;

  return (
    <div className="wf-pagination" aria-label="Workflow definition pagination">
      <span className="wf-pagination-summary" aria-live="polite">
        Showing {firstItem}-{lastItem}{hasKnownTotal ? ` of ${totalCount}` : ""}
      </span>
      <label className="wf-page-size">
        Rows
        <select value={pageSize} onChange={event => onPageSizeChange(Number(event.target.value))}>
          {definitionPageSizes.map(size => <option key={size} value={size}>{size}</option>)}
        </select>
      </label>
      <div className="wf-page-controls">
        <button type="button" onClick={() => onPageChange(page - 1)} disabled={page <= 1} aria-label="Previous page" title="Previous page">
          <ChevronLeft size={14} /> Previous
        </button>
        <span>Page {page}{totalPages ? ` of ${totalPages}` : ""}</span>
        <button type="button" onClick={() => onPageChange(page + 1)} disabled={totalPages !== null ? page >= totalPages : !hasNextPage} aria-label="Next page" title="Next page">
          Next <ChevronRight size={14} />
        </button>
      </div>
    </div>
  );
}
