import type * as Preset from '@docusaurus/preset-classic';
import type { Config } from '@docusaurus/types';
import { themes as prismThemes } from 'prism-react-renderer';

// The site renders the Markdown in ../docs, which stays the single source of truth that agents and
// GitHub read directly. Plans are working documents and are not published.
const config: Config = {
  title: 'AutoCanvas',
  tagline: 'Self-hosted automation and CI/CD — pipelines as code or on a canvas',
  favicon: 'img/favicon.svg',

  url: 'https://bexy315.github.io',
  baseUrl: '/autocanvas/',
  organizationName: 'Bexy315',
  projectName: 'autocanvas',
  trailingSlash: false,

  onBrokenLinks: 'throw',
  onBrokenAnchors: 'throw',
  markdown: {
    // Plain .md files are CommonMark, so docs written for GitHub render the same here.
    format: 'detect',
    hooks: {
      onBrokenMarkdownLinks: 'throw',
      onBrokenMarkdownImages: 'throw',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  plugins: [
    [
      // The HTTP API reference, rendered from the committed OpenAPI document. Both the document
      // and the viewer (the pinned @scalar/api-reference bundle) are served from this site by
      // scripts/copy-schemas.mjs: without `cdn`, the plugin would load the latest viewer from a
      // CDN on every visit. Everything that talks to Scalar's services is switched off.
      '@scalar/docusaurus',
      {
        label: 'API reference',
        route: '/api',
        showNavLink: true,
        cdn: '/autocanvas/scalar/standalone.js',
        configuration: {
          url: '/autocanvas/openapi/v1.json',
          // The document says "/" (wherever the server runs); here that would be the docs site.
          servers: [{ url: 'https://autocanvas.example.org', description: 'Your AutoCanvas server' }],
          hideClientButton: true,
          hideTestRequestButton: true,
          showDeveloperTools: 'never',
          showToolbar: 'never',
          telemetry: false,
          withDefaultFonts: false,
          agent: { disabled: true },
          mcp: { disabled: true },
        },
      },
    ],
  ],

  presets: [
    [
      'classic',
      {
        docs: {
          path: '../docs',
          routeBasePath: '/',
          sidebarPath: './sidebars.ts',
          exclude: ['plans/**'],
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'AutoCanvas',
      logo: {
        alt: 'AutoCanvas',
        src: 'img/favicon.svg',
      },
      items: [
        { type: 'docSidebar', sidebarId: 'guide', position: 'left', label: 'Guide' },
        { type: 'docSidebar', sidebarId: 'administration', position: 'left', label: 'Administration' },
        { type: 'docSidebar', sidebarId: 'development', position: 'left', label: 'Development' },
        {
          href: 'https://github.com/Bexy315/autocanvas',
          position: 'right',
          className: 'header-github-link',
          'aria-label': 'AutoCanvas on GitHub',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Documentation',
          items: [
            { label: 'Introduction', to: '/user/introduction' },
            { label: 'Your first automation', to: '/user/web-ui' },
            { label: 'Administration', to: '/user/installation' },
            { label: 'Development', to: '/technical/architecture' },
            { label: 'API reference', to: '/api' },
          ],
        },
        {
          title: 'Project',
          items: [
            { label: 'GitHub', href: 'https://github.com/Bexy315/autocanvas' },
            { label: 'Issues', href: 'https://github.com/Bexy315/autocanvas/issues' },
            { label: 'Decisions', to: '/adr' },
            { label: 'License (MIT)', href: 'https://github.com/Bexy315/autocanvas/blob/main/LICENSE' },
          ],
        },
      ],
      copyright:
        'AutoCanvas · MIT License · Pre-alpha, written almost entirely by AI coding agents: test it before you rely on it.',
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'yaml', 'json', 'protobuf'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
