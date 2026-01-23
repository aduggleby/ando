// Build table of contents from h2 elements
const tocList = document.getElementById("toc-list");
const content = document.getElementById("content");
const headings = content?.querySelectorAll("h2") || [];

headings.forEach((heading, index) => {
  // Add ID to heading if not present
  if (!heading.id) {
    heading.id =
      heading.textContent
        ?.toLowerCase()
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/(^-|-$)/g, "") || `section-${index}`;
  }

  const text = heading.textContent;
  if (!text) return;

  const li = document.createElement("li");
  const link = document.createElement("a");
  link.href = `#${heading.id}`;
  link.textContent = text;
  link.className =
    "toc-link block text-zinc-500 dark:text-zinc-400 hover:text-cyan-600 dark:hover:text-cyan-400 transition-colors";
  li.appendChild(link);
  tocList?.appendChild(li);
});

// Highlight active section on scroll
const tocLinks = document.querySelectorAll(".toc-link");

function updateActiveLink() {
  let currentHeading = "";
  headings.forEach((heading) => {
    const rect = heading.getBoundingClientRect();
    if (rect.top <= 100) {
      currentHeading = heading.id;
    }
  });

  tocLinks.forEach((link) => {
    const href = link.getAttribute("href");
    if (href === `#${currentHeading}`) {
      link.classList.add("text-cyan-600", "dark:text-cyan-400", "font-medium");
      link.classList.remove("text-zinc-500", "dark:text-zinc-400");
    } else {
      link.classList.remove("text-cyan-600", "dark:text-cyan-400", "font-medium");
      link.classList.add("text-zinc-500", "dark:text-zinc-400");
    }
  });
}

window.addEventListener("scroll", updateActiveLink);
updateActiveLink();
