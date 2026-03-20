document.addEventListener("DOMContentLoaded", () => {
  initializeDisabledActions(document);
  initializeEndpointSearch(document);
  initializeEndpointRefreshForms(document);
  applyPendingEndpointFlash(document);
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

function initializeEndpointRefreshForms(root) {
  root.querySelectorAll("[data-endpoint-refresh-form]").forEach((form) => {
    if (form.dataset.refreshFlashBound === "true") {
      return;
    }

    form.dataset.refreshFlashBound = "true";
    form.addEventListener("submit", () => {
      const endpointInput = form.querySelector("input[name='endpointId']");
      const endpointId = endpointInput ? endpointInput.value.trim() : "";

      if (endpointId !== "") {
        window.sessionStorage.setItem("dashboardPendingFlashEndpointId", endpointId);
      }
    });
  });
}

function applyPendingEndpointFlash(root) {
  const endpointId = window.sessionStorage.getItem("dashboardPendingFlashEndpointId");
  if (!endpointId) {
    return;
  }

  const row = root.querySelector(`[data-endpoint-row-id="${cssEscape(endpointId)}"]`);
  if (!row) {
    return;
  }

  flashEndpointRow(row);
  window.sessionStorage.removeItem("dashboardPendingFlashEndpointId");
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
    const previousRowStates = getEndpointRowStates(container);

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
      initializeEndpointRefreshForms(container);
      flashChangedEndpointRows(container, previousRowStates);
      applyPendingEndpointFlash(container);
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

function cssEscape(value) {
  if (window.CSS && typeof window.CSS.escape === "function") {
    return window.CSS.escape(value);
  }

  return value.replace(/["\\]/g, "\\$&");
}

function getEndpointRowStates(root) {
  const rowStates = new Map();

  root.querySelectorAll("[data-endpoint-row-id]").forEach((row) => {
    const endpointId = row.getAttribute("data-endpoint-row-id");
    if (!endpointId) {
      return;
    }

    rowStates.set(endpointId, {
      signature: row.getAttribute("data-endpoint-row-signature") || "",
      status: row.getAttribute("data-endpoint-row-status") || ""
    });
  });

  return rowStates;
}

function flashChangedEndpointRows(root, previousRowStates) {
  if (!previousRowStates || previousRowStates.size === 0) {
    return;
  }

  root.querySelectorAll("[data-endpoint-row-id]").forEach((row) => {
    const endpointId = row.getAttribute("data-endpoint-row-id");
    if (!endpointId || !previousRowStates.has(endpointId)) {
      return;
    }

    const previousState = previousRowStates.get(endpointId);
    const previousSignature = previousState ? previousState.signature : "";
    const previousStatus = previousState ? previousState.status : "";
    const currentSignature = row.getAttribute("data-endpoint-row-signature") || "";
    const currentStatus = row.getAttribute("data-endpoint-row-status") || "";

    if (previousSignature !== currentSignature) {
      const flashClass = getFlashClassForStatusChange(previousStatus, currentStatus);

      flashEndpointRow(row, flashClass);
    }
  });
}

function flashEndpointRow(row, flashClass = "dashboard-row-flash-update") {
  row.classList.remove(
    "dashboard-row-flash-update",
    "dashboard-row-flash-improving",
    "dashboard-row-flash-worsening"
  );
  void row.offsetWidth;
  row.classList.add(flashClass);

  window.setTimeout(() => {
    row.classList.remove(flashClass);
  }, 2200);
}

function getFlashClassForStatusChange(previousStatus, currentStatus) {
  if (previousStatus === currentStatus) {
    return "dashboard-row-flash-update";
  }

  const previousRank = getEndpointStatusRank(previousStatus);
  const currentRank = getEndpointStatusRank(currentStatus);

  if (currentRank > previousRank) {
    return "dashboard-row-flash-improving";
  }

  if (currentRank < previousRank) {
    return "dashboard-row-flash-worsening";
  }

  return "dashboard-row-flash-update";
}

function getEndpointStatusRank(status) {
  switch ((status || "").toLowerCase()) {
    case "healthy":
      return 3;
    case "degraded":
      return 2;
    case "unhealthy":
      return 1;
    case "unknown":
      return 0;
    default:
      return 0;
  }
}
