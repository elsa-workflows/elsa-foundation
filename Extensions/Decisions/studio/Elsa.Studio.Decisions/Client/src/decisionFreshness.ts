import { useEffect, useState } from "react";

/**
 * Bumps when the Studio tab is shown again so @latest pins and version lists re-resolve
 * after another tab publishes. In-flight loads are still deduplicated; settled results are not reused.
 */
export function useForegroundGeneration() {
  const [generation, setGeneration] = useState(0);
  useEffect(() => {
    const bump = () => setGeneration(value => value + 1);
    const onVisibility = () => {
      if (document.visibilityState === "visible") bump();
    };
    window.addEventListener("focus", bump);
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      window.removeEventListener("focus", bump);
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, []);
  return generation;
}
