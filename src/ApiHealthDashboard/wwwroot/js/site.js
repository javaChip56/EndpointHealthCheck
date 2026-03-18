document.addEventListener("DOMContentLoaded", () => {
  document.querySelectorAll("[aria-disabled='true']").forEach((element) => {
    element.addEventListener("click", (event) => {
      event.preventDefault();
    });
  });

  const searchInput = document.querySelector("[data-endpoint-search-input]");
  const searchRows = Array.from(document.querySelectorAll("[data-endpoint-search-row]"));
  const emptyRow = document.querySelector("[data-endpoint-search-empty]");
  const searchCount = document.querySelector("[data-endpoint-search-count]");

  if (!searchInput || searchRows.length === 0) {
    return;
  }

  const applyEndpointFilter = () => {
    const query = searchInput.value.trim().toLowerCase();
    let visibleCount = 0;

    searchRows.forEach((row) => {
      const searchText = (row.getAttribute("data-endpoint-search-text") || row.textContent || "").toLowerCase();
      const isVisible = query === "" || searchText.includes(query);
      row.hidden = !isVisible;

      if (isVisible) {
        visibleCount += 1;
      }
    });

    if (emptyRow) {
      emptyRow.hidden = query === "" || visibleCount > 0;
    }

    if (searchCount) {
      searchCount.textContent = query === "" ? "" : `${visibleCount} match${visibleCount === 1 ? "" : "es"}`;
    }
  };

  searchInput.addEventListener("input", applyEndpointFilter);
  searchInput.addEventListener("search", applyEndpointFilter);
});
