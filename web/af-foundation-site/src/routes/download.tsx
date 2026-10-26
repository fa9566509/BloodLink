import { createFileRoute } from '@tanstack/react-router';
import { site } from '../config/site';

export const Route = createFileRoute('/download')({
  component: DownloadPage,
});

function DownloadPage() {
  const { googlePlay, appStore } = site.appStores;

  return (
    <section className="section">
      <div className="container">
        <h1>Get the app</h1>
        <p>
          The AF Foundation app is used to register as a donor, manage your
          availability, respond to nearby requests and follow your donation
          history.
        </p>

        <h2>Android</h2>
        {googlePlay ? (
          <p>
            <a className="btn btn-primary" href={googlePlay}>
              Install from Google Play
            </a>
          </p>
        ) : (
          <p>
            The Android app is not yet published on Google Play. When it is,
            the official link will appear here. Please do not install APKs
            from unofficial sources.
          </p>
        )}

        <h2>iOS</h2>
        {appStore ? (
          <p>
            <a className="btn btn-primary" href={appStore}>
              Download on the App Store
            </a>
          </p>
        ) : (
          <p>
            The iOS app is not yet published on the App Store. When it is, the
            official link will appear here.
          </p>
        )}

        <h2>After you install</h2>
        <ol className="steps">
          <li>Register an account and verify your email or phone.</li>
          <li>Create your donor profile with your blood group.</li>
          <li>
            Choose whether to enable discovery — the app will ask for location
            permission only if you do, and you can also set your approximate
            location manually.
          </li>
          <li>Toggle your availability whenever your circumstances change.</li>
        </ol>

        <h2>Notifications</h2>
        <p>
          Push notifications tell you about nearby requests and updates to the
          ones you follow. If you decline notification permission the app
          still works — your notification inbox inside the app always contains
          the same information.
        </p>
      </div>
    </section>
  );
}
