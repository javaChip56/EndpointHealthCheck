document.addEventListener("DOMContentLoaded", () => {
  initializeDisabledActions(document);
  initializeEndpointSearch(document);
  initializeCopyButtons(document);
  initializeDashboardSectionRefresh();
});

function initializeDisabledActions(root) {
  root.querySelectorAll("[aria-disabled='true']").forEach((element) => {
    if (element.dataset.disabledActionBound === "true") {
      return;
    }

    element.dataset.disabledActionBound = "true";
    element.addEventListener("click", (event) => {
      event.preventDefault();
    });
  });
}

function initializeEndpointSearch(root, preservedQuery) {
  const searchInput = root.querySelector("[data-endpoint-search-input]");
  const searchRows = Array.from(root.querySelectorAll("[data-endpoint-search-row]"));
  const emptyRow = root.querySelector("[data-endpoint-search-empty]");
  const searchCount = root.querySelector("[data-endpoint-search-count]");

  if (!searchInput || searchRows.length === 0) {
    return;
  }

  if (typeof preservedQuery === "string") {
    searchInput.value = preservedQuery;
  }

  if (searchInput.dataset.searchBound === "true") {
    applyEndpointFilter(searchInput, searchRows, emptyRow, searchCount);
    return;
  }

  searchInput.dataset.searchBound = "true";

  const applyFilter = () => {
    applyEndpointFilter(searchInput, searchRows, emptyRow, searchCount);
  };

  searchInput.addEventListener("input", applyFilter);
  searchInput.addEventListener("search", applyFilter);
  applyFilter();
}

function applyEndpointFilter(searchInput, searchRows, emptyRow, searchCount) {
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
}

function initializeCopyButtons(root) {
  root.querySelectorAll("[data-copy-target]").forEach((button) => {
    if (button.dataset.copyBound === "true") {
      return;
    }

    button.dataset.copyBound = "true";
    button.addEventListener("click", async () => {
      const targetSelector = button.getAttribute("data-copy-target");
      const feedbackSelector = button.getAttribute("data-copy-feedback");
      const target = targetSelector ? document.querySelector(targetSelector) : null;
      const feedback = feedbackSelector ? document.querySelector(feedbackSelector) : null;

      if (!target) {
        setCopyFeedback(feedback, "Nothing to copy.");
        return;
      }

      const text = target.textContent || "";
      if (text.trim() === "") {
        setCopyFeedback(feedback, "Nothing to copy.");
        return;
      }

      try {
        await navigator.clipboard.writeText(text);
        setCopyFeedback(feedback, "Copied.");
      } catch {
        setCopyFeedback(feedback, "Copy failed.");
      }
    });
  });
}

function setCopyFeedback(feedback, message) {
  if (feedback) {
    feedback.textContent = message;
  }
}

function initializeDashboardSectionRefresh() {
  const container = document.querySelector("[data-dashboard-live-section]");
  if (!container) {
    return;
  }

  const refreshUrl = container.getAttribute("data-refresh-url");
  const refreshSeconds = Number.parseInt(container.getAttribute("data-refresh-seconds") || "", 10);

  if (!refreshUrl || !Number.isFinite(refreshSeconds) || refreshSeconds <= 0) {
    return;
  }

  window.setInterval(async () => {
    if (shouldPauseDashboardRefresh(container)) {
      return;
    }

    const preservedQuery = getCurrentSearchQuery(container);

    try {
      const response = await fetch(refreshUrl, {
        method: "GET",
        headers: {
          "X-Requested-With": "XMLHttpRequest"
        },
        credentials: "same-origin",
        cache: "no-store"
      });

      if (!response.ok) {
        return;
      }

      const html = await response.text();
      container.innerHTML = html;

      initializeDisabledActions(container);
      initializeEndpointSearch(container, preservedQuery);
      initializeCopyButtons(container);
    } catch {
      // Keep the current rendered section if the background refresh fails.
    }
  }, refreshSeconds * 1000);
}

function getCurrentSearchQuery(container) {
  const searchInput = container.querySelector("[data-endpoint-search-input]");
  return searchInput ? searchInput.value : "";
}

function shouldPauseDashboardRefresh(container) {
  const activeElement = document.activeElement;
  if (!activeElement || !container.contains(activeElement)) {
    return false;
  }

  return activeElement.matches("input, textarea, select");
}
