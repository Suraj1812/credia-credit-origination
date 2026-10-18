/* Lightweight animate-on-scroll: no network dependency, no blocked content without JS. */
(() => {
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  if (reduced.matches || !('IntersectionObserver' in window)) return;
  const selector = '.page-heading, .metric-card, .panel, .guide-section, .integration-card, .policy-banner, .document-card, .estimate-card, .login-story-inner, .login-form';
  const seen = new WeakSet();
  const reveal = new IntersectionObserver(entries => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      entry.target.classList.add('is-visible');
      reveal.unobserve(entry.target);
    }
  }, { rootMargin: '0px 0px -48px 0px', threshold: 0.06 });
  const register = () => {
    document.querySelectorAll(selector).forEach((element, index) => {
      if (seen.has(element)) return;
      seen.add(element);
      element.classList.add('motion-reveal');
      element.style.setProperty('--reveal-delay', `${Math.min(index % 5, 4) * 55}ms`);
      reveal.observe(element);
    });
  };
  let frame = 0;
  const schedule = () => { if (!frame) frame = requestAnimationFrame(() => { frame = 0; register(); }); };
  document.addEventListener('DOMContentLoaded', register, { once: true });
  if (document.readyState !== 'loading') register();
  new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true });
  document.addEventListener('click', event => {
    const anchor = event.target.closest?.('a[href^="#"]');
    if (!anchor) return;
    const id = anchor.getAttribute('href')?.slice(1);
    const target = id && document.getElementById(id);
    if (!target) return;
    event.preventDefault();
    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
    history.replaceState(null, '', `#${id}`);
  });
})();
