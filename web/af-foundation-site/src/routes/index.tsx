import { Link, createFileRoute } from '@tanstack/react-router';
import { site } from '../config/site';

export const Route = createFileRoute('/')({
  component: HomePage,
});

function StoreButtons() {
  const { googlePlay, appStore } = site.appStores;
  return (
    <div className="cta-row">
      {googlePlay ? (
        <a className="btn btn-primary" href={googlePlay}>
          Get it on Google Play
        </a>
      ) : (
        <span className="btn btn-secondary" aria-disabled="true">
          Android app — coming to Google Play
        </span>
      )}
      {appStore ? (
        <a className="btn btn-primary" href={appStore}>
          Download on the App Store
        </a>
      ) : (
        <span className="btn btn-secondary" aria-disabled="true">
          iOS app — coming to the App Store
        </span>
      )}
    </div>
  );
}

function HomePage() {
  return (
    <>
      <section className="hero">
        <div className="container">
          <h1>{site.tagline}</h1>
          <p className="lead">
            {site.foundationName} is building a blood donation coordination
            service for Pakistan. Register as a potential donor, and when
            someone near you needs blood, we help connect you — quickly, and
            with your consent at every step.
          </p>
          <div className="cta-row">
            <Link className="btn btn-primary" to="/download">
              Become a donor
            </Link>
            <Link className="btn btn-secondary" to="/how-it-works">
              How it works
            </Link>
          </div>
        </div>
      </section>

      <section className="section">
        <div className="container">
          <h2>What the service does</h2>
          <div className="grid">
            <div className="card">
              <h3>Register once</h3>
              <p>
                Create an account, add your blood group and an approximate
                location. You decide when you are discoverable and available.
              </p>
            </div>
            <div className="card">
              <h3>Get matched nearby</h3>
              <p>
                When a whole-blood request opens near you, eligible compatible
                donors are notified in expanding waves around the request.
              </p>
            </div>
            <div className="card">
              <h3>Respond on your terms</h3>
              <p>
                Accept or decline from the app. Accepting means you are willing
                to help — coordination happens directly with the requester.
              </p>
            </div>
            <div className="card">
              <h3>Transparent eligibility</h3>
              <p>
                Reported donations are recorded with their verification status,
                and any deferral countdown is shown openly in the app.
              </p>
            </div>
          </div>
        </div>
      </section>

      <section className="section section-alt">
        <div className="container">
          <h2>Get the app</h2>
          <p>
            The service runs through the AF Foundation mobile app for Android
            and iOS.
          </p>
          <StoreButtons />
          <p style={{ marginTop: '1rem' }}>
            Apps not published yet? <Link to="/download">See installation guidance</Link>.
          </p>
        </div>
      </section>

      <section className="section">
        <div className="container">
          <div className="notice">
            <strong>Medical disclaimer.</strong> This service coordinates
            discovery and communication only. It does not operate a blood
            bank, does not certify that any donor is medically eligible, and
            does not replace professional screening at the donation centre.
            Final eligibility is always determined by qualified transfusion
            professionals.
          </div>
        </div>
      </section>
    </>
  );
}
