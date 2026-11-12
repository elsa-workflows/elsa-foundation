import React from "react";

/**
 * Recessed search field. It renders a plain bordered input on flat themes; under
 * `[data-theme-material]` the `.studio-search` shell reads as a sunken well (via the
 * per-theme soft panel recipe + an inset shadow) so it reads as carved into the slab
 * rather than floating on it. `type=search` and `role=searchbox` keep it announced
 * correctly; the visible icon is decorative.
 */
export function StudioSearchInput({
  className,
  ...rest
}: React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <div className={className ? `studio-search ${className}` : "studio-search"}>
      <svg className="studio-search-icon" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
        <circle cx="7" cy="7" r="4.5" fill="none" stroke="currentColor" strokeWidth="1.5" />
        <line x1="10.5" y1="10.5" x2="14" y2="14" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      </svg>
      <input type="search" role="searchbox" className="studio-search-input" {...rest} />
    </div>
  );
}
