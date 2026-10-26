import { Link, Outlet, createRootRoute } from '@tanstack/react-router';
import { site } from '../config/site';

export const Route = createRootRoute({
  component: RootLayout,
});

const navLinks = [
  { to: '/how-it-works', label: 'How it works' },
  { to: '/download', label: 'Get the app' },
  { to: '/faq', label: 'FAQs' },
  { to: '/safety', label: 'Safety' },
  { to: '/contact', label: 'Contact' },
];

function RootLayout() {
  return (
    <>
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <header className="site-header">
        <div className="container">
          <Link to="/" className="brand">
            <span className="brand-mark" aria-hidden="true">
              +
            </span>
            {site.foundationName}
          </Link>
          <nav className="nav" aria-label="Main navigation">
            {navLinks.map((l) => (
              <Link key={l.to} to={l.to}>
                {l.label}
              </Link>
            ))}
          </nav>
        </div>
      </header>

      <main id="main">
        <Outlet />
      </main>

      <footer className="site-footer">
        <div className="container">
          <div className="footer-links">
            <Link to="/privacy">Privacy policy</Link>
            <Link to="/terms">Terms of use</Link>
            <Link to="/safety">Donation safety</Link>
            <Link to="/faq">FAQs</Link>
            <Link to="/contact">Contact</Link>
          </div>
          <p>
            © {new Date().getFullYear()} {site.foundationName}. A coordination
            service only — it does not replace professional screening at a
            donation centre.
          </p>
        </div>
      </footer>
    </>
  );
}
