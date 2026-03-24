export default function ConnectionStatus({ connected }: { connected: boolean }) {
  return (
    <div className={`live-badge ${connected ? 'live' : 'offline'}`}>
      <span className="live-dot" />
    </div>
  )
}
