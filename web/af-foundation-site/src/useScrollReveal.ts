import { useEffect } from "react";

/** One-time, progressive enhancement: content stays readable if motion cannot run. */
export function useScrollReveal() {
  useEffect(() => {
    if (!("IntersectionObserver" in window)) return;
    const preference = window.matchMedia("(prefers-reduced-motion: reduce)");
    const animations = new Map<Element, Animation>();
    const targets = document.querySelectorAll<HTMLElement>(
      ".journey-section .section-heading, .journey-item, .care-art, .care-layout > div:last-child, .home-faq > div, .sponsors-section .section-heading, .sponsor-card, .thank-you-section, .join-section",
    );
    const observer = new IntersectionObserver((entries) => {
      entries.forEach(({ target, isIntersecting }) => {
        if (!isIntersecting) return;
        observer.unobserve(target);
        if (target.contains(document.activeElement)) return;
        const siblings = [...target.parentElement!.children];
        const stagger = target.matches(".journey-item, .sponsor-card")
          ? siblings.indexOf(target) * 60 : 0;
        const animation = target.animate(
          preference.matches
            ? [{ opacity: 0.6 }, { opacity: 1 }]
            : [{ opacity: 0, transform: "translateY(16px)" }, { opacity: 1, transform: "translateY(0)" }],
          { duration: preference.matches ? 200 : 600, delay: preference.matches ? 0 : stagger,
            easing: getComputedStyle(document.documentElement).getPropertyValue("--ease-out").trim(), fill: "backwards" },
        );
        animations.set(target, animation);
        animation.onfinish = () => animations.delete(target);
      });
    }, { threshold: 0.08, rootMargin: "0px 0px -32px 0px" });
    targets.forEach((target) => observer.observe(target));
    const cancelMotion = () => animations.forEach((animation) => animation.cancel());
    const onFocus = (event: FocusEvent) => animations.forEach((animation, target) => {
      if (event.target instanceof Node && target.contains(event.target)) animation.cancel();
    });
    preference.addEventListener("change", cancelMotion);
    document.addEventListener("focusin", onFocus);
    return () => {
      observer.disconnect();
      cancelMotion();
      preference.removeEventListener("change", cancelMotion);
      document.removeEventListener("focusin", onFocus);
    };
  }, []);
}
