import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import Layout from '@theme/Layout';
import ThemedImage from '@theme/ThemedImage';
import type { ReactNode } from 'react';
import styles from './index.module.css';

interface Feature {
  title: string;
  text: string;
  picture: string;
  alt: string;
  to: string;
}

const features: Feature[] = [
  {
    title: 'Pipelines as code or on a canvas',
    text: 'An automation is one YAML file. Write it in your repository or build it on the canvas: both edit the same file, comments and all.',
    picture: 'tour-editor',
    alt: 'The editor with the canvas and the YAML file side by side',
    to: '/user/web-ui',
  },
  {
    title: 'Runners where the work is',
    text: 'Jobs run in containers on runners you place, with services beside them and a live log you can follow.',
    picture: 'tour-run-log',
    alt: 'A run with its jobs and the live log',
    to: '/user/runs',
  },
  {
    title: 'Test reports built in',
    text: 'JUnit, TRX and Cobertura become test results, history, flaky tests and coverage for every run, without plugins.',
    picture: 'tour-tests',
    alt: 'The failed test of a run with its history',
    to: '/user/reports',
  },
  {
    title: 'Repositories and GitHub',
    text: 'Pushes and pull requests start runs; commit statuses and a pull request comment report back.',
    picture: 'repositories',
    alt: 'A folder with a connected repository',
    to: '/user/repositories',
  },
];

function Picture({ name, alt }: { name: string; alt: string }) {
  return (
    <ThemedImage
      className={styles['picture']}
      alt={alt}
      sources={{ light: useBaseUrl(`/img/ui/${name}-light.png`), dark: useBaseUrl(`/img/ui/${name}-dark.png`) }}
    />
  );
}

export default function Home(): ReactNode {
  return (
    <Layout description="Self-hosted automation and CI/CD: pipelines as code or on a canvas, runners, built-in test reports.">
      <header className={styles['hero']}>
        <div className="container">
          <h1 className={styles['title']}>AutoCanvas</h1>
          <p className={styles['tagline']}>
            Self-hosted automation and CI/CD for developers and technicians: pipelines as code or on a canvas,
            lightweight runners, and test reports built in.
          </p>
          <div className={styles['actions']}>
            <Link className="button button--primary button--lg" to="/user/introduction">
              Get started
            </Link>
            <Link className="button button--secondary button--lg" to="/user/web-ui">
              Your first automation
            </Link>
            <Link className="button button--outline button--secondary button--lg" href="https://github.com/Bexy315/autocanvas">
              GitHub
            </Link>
          </div>
          <p className={styles['note']}>
            Pre-alpha. AutoCanvas grew out of the maintainer&apos;s homelab experiments and has been written almost
            entirely by AI coding agents: test it before you rely on it.
          </p>
          <div className={styles['heroPicture']}>
            <Picture name="tour-editor" alt="The AutoCanvas editor: canvas and YAML side by side" />
          </div>
        </div>
      </header>
      <main className="container">
        <section className={styles['features']} aria-label="What AutoCanvas does">
          {features.slice(1).map((feature) => (
            <article key={feature.title} className={styles['feature']}>
              <Link to={feature.to} className={styles['featurePicture']}>
                <Picture name={feature.picture} alt={feature.alt} />
              </Link>
              <h2 className={styles['featureTitle']}>
                <Link to={feature.to}>{feature.title}</Link>
              </h2>
              <p>{feature.text}</p>
            </article>
          ))}
        </section>
        <section className={styles['paths']} aria-label="Where to start">
          <Link to="/user/introduction" className={styles['path']}>
            <strong>Use it</strong>
            <span>Automations, triggers, runs, reports, folders and repositories.</span>
          </Link>
          <Link to="/user/installation" className={styles['path']}>
            <strong>Run it</strong>
            <span>Installation, configuration, deployment, operations and security.</span>
          </Link>
          <Link to="/technical/architecture" className={styles['path']}>
            <strong>Change it</strong>
            <span>Architecture, the HTTP API, the runner protocol and the decisions behind them.</span>
          </Link>
        </section>
      </main>
    </Layout>
  );
}
