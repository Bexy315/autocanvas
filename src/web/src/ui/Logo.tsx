/** The AutoCanvas mark: three connected nodes, as on the canvas. */
export function LogoMark({ size = 24 }: { size?: number }) {
  return (
    <svg aria-hidden="true" width={size} height={size} viewBox="0 0 32 32" className="shrink-0">
      <rect width="32" height="32" rx="7" className="fill-accent" />
      <g className="fill-accent-ink stroke-accent-ink">
        <circle cx="9" cy="10" r="3" stroke="none" />
        <circle cx="23" cy="16" r="3" stroke="none" />
        <circle cx="9" cy="22" r="3" stroke="none" />
        <path d="M12 10.5 20 15M12 21.5 20 17" fill="none" strokeWidth="2" strokeLinecap="round" />
      </g>
    </svg>
  );
}
