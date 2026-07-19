import { ChevronLeft, ChevronRight } from "lucide-react";
import { definitionPageSizes } from "./constants";
import { getTotalPages } from "./editorHelpers";

export function DefinitionPager({ page, pageSize, totalCount, onPageChange, onPageSizeChange }: {
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange(page: number): void;
  onPageSizeChange(pageSize: number): void;
}) {
  const totalPages = getTotalPages(totalCount, pageSize);
  const firstItem = totalCount === 0 ? 0 : ((page - 1) * pageSize) + 1;
  const lastItem = Math.min(page * pageSize, totalCount);

  return (
    <div className="wf-pagination" aria-label="Workflow definition pagination">
      <span className="wf-pagination-summary" aria-live="polite">
        Showing {firstItem}-{lastItem} of {totalCount}
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
        <span>Page {page} of {totalPages}</span>
        <button type="button" onClick={() => onPageChange(page + 1)} disabled={page >= totalPages} aria-label="Next page" title="Next page">
          Next <ChevronRight size={14} />
        </button>
      </div>
    </div>
  );
}
