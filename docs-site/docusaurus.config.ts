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
        { type: 'docSidebar', sidebarId: 'user', position: 'left', label: 'User guide' },
        { type: 'docSidebar', sidebarId: 'technical', position: 'left', label: 'Technical' },
        { type: 'docSidebar', sidebarId: 'protocol', position: 'left', label: 'Protocol' },
        { type: 'docSidebar', sidebarId: 'adr', position: 'left', label: 'Decisions' },
      ],
    },
    footer: {
      style: 'dark',
      copyright: `AutoCanvas · MIT License`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'yaml', 'json', 'protobuf'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
