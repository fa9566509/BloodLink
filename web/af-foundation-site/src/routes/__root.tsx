import { ArrowUpRight, Asterisk } from "lucide-react";
import { Link, Outlet, createRootRoute } from "@tanstack/react-router";
export const Route = createRootRoute({ component: RootLayout });
function Brand() {
  return (
    <Link to="/" className="brand" aria-label="AF Foundation home">
      <span className="brand-mark" aria-hidden="true">
        af<span><Asterisk size={23} strokeWidth={1.5} /></span>
      </span>
      <span>
        AF Foundation<small>A CONNECTION THAT COUNTS</small>
      </span>
    </Link>
  );
}
function RootLayout() {
  return (
    <>
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <header className="site-header">
        <div className="reading-progress" aria-hidden="true" />
        <div className="container header-inner">
          <Brand />
          <nav className="nav" aria-label="Main navigation">
            <Link to="/how-it-works">How it works</Link>
            <Link to="/safety">Our commitment</Link>
            <Link to="/faq">FAQs</Link>
            <Link to="/contact">Get in touch</Link>
          </nav>
          <Link to="/download" className="btn header-cta">
            Join the circle <ArrowUpRight size={18} aria-hidden="true" />
          </Link>
        </div>
      </header>
      <main id="main">
        <Outlet />
      </main>
      <footer className="site-footer">
        <div className="container">
          <div className="footer-top">
            <Brand />
            <p>
              Small acts.
              <br />
              <em>Extraordinary connections.</em>
            </p>
          </div>
          <div className="footer-bottom">
            <span>
              © {new Date().getFullYear()} AF Foundation · Made for Pakistan.
            </span>
            <div className="footer-links">
              <Link to="/privacy">Privacy</Link>
              <Link to="/terms">Terms</Link>
              <Link to="/safety">Donation safety</Link>
              <Link to="/contact">Contact</Link>
            </div>
          </div>
        </div>
      </footer>
    </>
  );
}
