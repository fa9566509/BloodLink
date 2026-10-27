import { useState } from "react";
import { ArrowUpRight, ArrowRight, Heart, HeartHandshake, Plus, Radar, UserPlus, ShieldCheck, Sparkles } from "lucide-react";
import { useScrollReveal } from "../useScrollReveal";
import { Link, createFileRoute } from "@tanstack/react-router";
export const Route = createFileRoute("/")({ component: HomePage });
const waves = [25, 50, 100];
const dots = [
  [46, 36, 0],
  [59, 47, 0],
  [39, 56, 0],
  [65, 28, 1],
  [27, 40, 1],
  [68, 65, 1],
  [32, 72, 1],
  [79, 43, 2],
  [21, 65, 2],
  [51, 17, 2],
  [57, 82, 2],
];
function MatchingPreview() {
  const [wave, setWave] = useState(0);
  return (
    <div className="matching-preview">
      <div className="preview-top">
        <span>
          <i className="status-dot" /> A little closer to help
        </span>
        <span className="small-label">HOW MATCHING WORKS</span>
      </div>
      <div
        className="map-art"
        role="img"
        aria-label={`Illustration of a ${waves[wave]} kilometre search radius. Not real donor locations.`}
      >
        <svg
          className="map-roads"
          viewBox="0 0 500 430"
          fill="none"
          aria-hidden="true"
        >
          <path d="M-20 110L150 165L285 110L520 180M70-20L150 165L95 320L165 460M285-20L285 110L340 280L270 460M-20 350L95 320L340 280L520 330M-20 235L200 235L430 40M200 235L240 460" />
          <path
            className="river"
            d="M470-20C340 80 480 130 390 210S410 350 340 460"
          />
        </svg>
        <span className="map-place place-one">YOUR COMMUNITY</span>
        <span className="map-place place-two">NEARBY, NOT FAR AWAY</span>
        <div className="search-ring ring-outer" />
        <div className="search-ring ring-middle" />
        <div
          className="search-area"
          style={{
            transform: `translate(-50%, -50%) scale(${[0.46, 0.72, 1][wave]})`,
          }}
        />
        {dots.map(([x, y, l], i) => (
          <span
            key={i}
            className="donor-dot"
            data-active={l <= wave}
            style={{ left: `${x}%`, top: `${y}%` }}
          />
        ))}
        <div className="hospital-pin" aria-hidden="true">
          <Plus size={28} aria-hidden="true" />
        </div>
        <div className="map-caption">
          <span className="caption-icon"><ArrowUpRight aria-hidden="true" size={20} /></span>
          <div>
            <strong>One request. A circle of care.</strong>
            <span>Compatible donors, closer to you.</span>
          </div>
        </div>
      </div>
      <div className="wave-control">
        <div>
          <span className="small-label">EXPLORE THE SEARCH</span>
          <p>Help starts nearby. Then reaches further.</p>
        </div>
        <div
          className="wave-buttons"
          role="group"
          aria-label="Illustrative search radius"
        >
          {waves.map((r, i) => (
            <button
              key={r}
              aria-pressed={wave === i}
              onClick={() => setWave(i)}
            >
              {r}
              <span> km</span>
            </button>
          ))}
        </div>
      </div>
      <p className="preview-disclaimer">
        Illustration only · no real requests or donor locations
      </p>
    </div>
  );
}
function HomePage() {
  useScrollReveal();
  return (
    <>
      <section className="home-hero container">
        <div className="hero-copy">
          <span className="eyebrow">
            <span /> PEOPLE HELPING PEOPLE · PAKISTAN
          </span>
          <h1>
            Someone nearby.
            <br />
            Someone’s <em>lifeline.</em>
          </h1>
          <p className="lead">
            A small act of kindness can mean everything. We’re bringing willing
            blood donors and people who need them a little closer together.
          </p>
          <div className="cta-row">
            <Link className="btn btn-primary" to="/download">
              Be there for someone <ArrowUpRight aria-hidden="true" size={18} />
            </Link>
            <Link className="text-link" to="/how-it-works">
              See how it works <ArrowRight aria-hidden="true" size={18} />
            </Link>
          </div>
          <div className="hero-note">
            <span className="note-mark" aria-hidden="true">
              <Heart size={28} />
            </span>
            <p>
              Always voluntary. Always your choice.
              <br />
              <strong>A community built on care, not obligation.</strong>
            </p>
          </div>
        </div>
        <MatchingPreview />
      </section>
      <div className="principles-strip">
        <div className="container">
          <span>
            <b>01</b> Your consent comes first
          </span>
          <span>
            <b>02</b> Your location stays private
          </span>
          <span>
            <b>03</b> Free to connect. Free to care.
          </span>
        </div>
      </div>
      <section className="journey-section section container">
        <div className="section-heading">
          <div>
            <span className="eyebrow">A SIMPLE WAY TO SHOW UP</span>
            <h2>
              From “I can help”
              <br />
              to <em>being there.</em>
            </h2>
          </div>
          <p>
            No complicated process. Just a thoughtful connection, with you in
            control at every step.
          </p>
        </div>
        <div className="journey-grid">
          {([
            [
              "01",
              UserPlus,
              "Make yourself known.",
              "Add your blood group and approximate area in the app. Choose when you’re available to help.",
              "A few details. A real difference.",
            ],
            [
              "02",
              Radar,
              "Find a connection.",
              "When a compatible request opens nearby, we’ll let you know. Accept if you can. Decline if you can’t.",
              "The choice is always yours.",
            ],
            [
              "03",
              HeartHandshake,
              "Show up with care.",
              "Coordinate with the requester and visit the donation centre, where professionals handle all screening.",
              "Human connection. Professional care.",
            ],
          ] as const).map(([n, Icon, title, body, tag]) => (
            <article className="journey-item" key={n}>
              <span className="step-index">{n} /</span>
              <div className="step-symbol" aria-hidden="true">
                <Icon size={40} strokeWidth={1.5} />
              </div>
              <h3>{title}</h3>
              <p>{body}</p>
              <span className="step-tag">{tag}</span>
            </article>
          ))}
        </div>
        <Link className="text-link" to="/how-it-works">
          The whole journey, explained <ArrowUpRight aria-hidden="true" size={18} />
        </Link>
      </section>
      <section className="care-section">
        <div className="container care-layout">
          <div className="care-art" aria-hidden="true">
            <span className="care-orbit"><Sparkles className="orbit-sparkle" size={32} /></span>
            <Heart className="care-heart" strokeWidth={1} />
            <span className="care-stamp">
              BUILT AROUND
              <br />
              <b>your choice.</b>
            </span>
          </div>
          <div>
            <span className="eyebrow">
              GOOD INTENTIONS. THOUGHTFUL SAFEGUARDS.
            </span>
            <h2>Care goes both ways.</h2>
            <p>
              Helping someone shouldn’t mean giving up your privacy or peace of
              mind.
            </p>
            {[
              [
                "Nearby doesn’t mean exposed.",
                "We store only an approximate location. Other users never see donor locations.",
              ],
              [
                "Your availability, on your terms.",
                "Switch discovery off at any time. Your stored approximate location is deleted.",
              ],
              [
                "Clear records. No assumptions.",
                "Reported and verified donations stay distinct. Final eligibility always rests with the donation centre.",
              ],
            ].map(([title, body]) => (
              <div className="care-row" key={title}>
                <ShieldCheck className="care-row-icon" size={22} aria-hidden="true" />
                <div>
                  <h3>{title}</h3>
                  <p>{body}</p>
                </div>
              </div>
            ))}
            <Link className="text-link" to="/safety">
              Our approach to safety <ArrowUpRight aria-hidden="true" size={18} />
            </Link>
          </div>
        </div>
      </section>
      <section className="section container home-faq">
        <div>
          <span className="eyebrow">A LITTLE CLARITY</span>
          <h2>
            It’s okay to{" "}
            <br />
            <em>have questions.</em>
          </h2>
          <Link className="text-link" to="/faq">
            All your questions <ArrowUpRight aria-hidden="true" size={18} />
          </Link>
        </div>
        <div className="faq">
          <details>
            <summary>Do I have to donate if I’m notified?<Plus className="faq-icon" size={20} aria-hidden="true" /></summary>
            <p>
              No. A notification is an invitation to help. You can accept or
              decline, and the donation centre determines whether you can safely
              donate.
            </p>
          </details>
          <details>
            <summary>Can anyone see where I live?<Plus className="faq-icon" size={20} aria-hidden="true" /></summary>
            <p>
              No. Only an approximate point is stored for matching, and donor
              locations are never shown to other users.
            </p>
          </details>
          <details>
            <summary>Is the app available yet?<Plus className="faq-icon" size={20} aria-hidden="true" /></summary>
            <p>
              The Android and iOS apps are not published yet. Official store
              links will appear on our download page when they’re ready.
            </p>
          </details>
        </div>
      </section>
      <section className="section container sponsors-section" id="sponsors" aria-labelledby="sponsors-title">
        <div className="section-heading">
          <div>
            <span className="eyebrow">OUR SPONSORS</span>
            <h2 id="sponsors-title">Good people. <em>Shared purpose.</em></h2>
          </div>
          <p>With support from people who believe in bringing our community closer together.</p>
        </div>
        <div className="sponsor-grid">
          {["Audomatick", "FA Consultants"].map((name) => (
            <article className="sponsor-card" key={name}>
              <span className="eyebrow">COMMUNITY SPONSOR</span>
              <h3>{name}</h3>
              <HeartHandshake className="sponsor-mark" size={22} aria-hidden="true" />
            </article>
          ))}
        </div>
      </section>
      <section className="container thank-you-section" aria-labelledby="thank-you-title">
        <Heart className="thank-you-heart" strokeWidth={1.25} aria-hidden="true" />
        <div>
          <span className="eyebrow">WITH GRATITUDE</span>
          <h2 id="thank-you-title">A heartfelt <em>thank you.</em></h2>
          <p>To <strong>Audomatick</strong> and <strong>FA Consultants</strong> — thank you for supporting this mission. And to every donor, volunteer, and neighbour willing to help: this community grows because of you.</p>
        </div>
      </section>
      <section className="join-section container">
        <div>
          <span className="eyebrow">A LITTLE OF YOU. A LOT OF HOPE.</span>
          <h2>
            You could be the person
            <br />
            someone is <em>hoping for.</em>
          </h2>
          <p>
            Start with a willingness to help. We’ll help with the connection.
          </p>
          <Link className="btn btn-light" to="/download">
            Meet the AF Foundation app <ArrowUpRight aria-hidden="true" size={18} />
          </Link>
          <span className="coming-soon">COMING SOON FOR ANDROID & iOS</span>
        </div>
        <span className="join-flower" aria-hidden="true">
          <Sparkles size={180} strokeWidth={1} />
        </span>
      </section>
      <div className="medical-note container">
        <ShieldCheck size={22} aria-hidden="true" />
        <p>
          A connection, not a medical decision. AF Foundation coordinates donor
          discovery only. We don’t operate a blood bank or certify eligibility.
          Every donation requires professional screening at a donation centre.
        </p>
      </div>
    </>
  );
}
