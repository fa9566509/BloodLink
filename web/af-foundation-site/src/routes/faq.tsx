import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/faq')({
  component: FaqPage,
});

const faqs: Array<{ q: string; a: string }> = [
  {
    q: 'Is donating blood safe?',
    a: 'For most healthy adults, whole-blood donation is a safe, routine procedure when performed at a qualified donation centre. The centre screens every donor before each donation — our service plays no part in that medical decision.',
  },
  {
    q: 'How often can I donate whole blood?',
    a: 'Minimum intervals differ by country, sex and individual circumstances, and are enforced by the donation centre at the point of donation. The app shows a recorded deferral countdown after a confirmed donation, but the countdown ending is not a medical clearance — final eligibility is determined by the donation centre.',
  },
  {
    q: 'Who can see my location?',
    a: 'Nobody sees your precise location. When you enable discovery, the app stores an approximate point (generalised to roughly a one-kilometre grid) that is used only to compute distances to requests. Other users never see donor locations at all.',
  },
  {
    q: 'What happens when I accept a request?',
    a: 'Accepting means you are willing to help. The requester is notified so you can coordinate where and when to go. Acceptance does not obligate you medically, and it never starts a donation deferral — only a recorded donation can do that.',
  },
  {
    q: 'What blood groups can donate to each other?',
    a: 'Matching follows the standard red-cell compatibility rules: O- donors can help everyone, and AB+ recipients can receive from every group. Rh-positive recipients can receive Rh-negative blood; the reverse is not safe.',
  },
  {
    q: 'Does the service charge money?',
    a: 'No. The service is a free coordination platform. Blood donation in Pakistan is voluntary and non-remunerated.',
  },
  {
    q: 'I disabled discovery — am I invisible?',
    a: 'Yes. Switching discovery off removes you from all new matching immediately, and your stored approximate location is deleted from our database.',
  },
  {
    q: 'How do I report abuse?',
    a: 'Every request and profile can be reported from the app. Reports go to our moderation team, who can suspend accounts. Serious concerns can also be sent through the contact page.',
  },
];

function FaqPage() {
  return (
    <section className="section">
      <div className="container faq">
        <h1>Frequently asked questions</h1>
        {faqs.map((f) => (
          <details key={f.q}>
            <summary>{f.q}</summary>
            <p>{f.a}</p>
          </details>
        ))}
      </div>
    </section>
  );
}
