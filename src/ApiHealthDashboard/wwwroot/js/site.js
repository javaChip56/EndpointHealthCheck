document.addEventListener("DOMContentLoaded", () => {
  document.querySelectorAll("[aria-disabled='true']").forEach((element) => {
    element.addEventListener("click", (event) => {
      event.preventDefault();
    });
  });
});
