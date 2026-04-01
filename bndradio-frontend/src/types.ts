// Shared TypeScript types used across frontend components and hooks.
export interface Song {
  id: string;
  title: string;
  artist: string;
  durationMs: number;
  playCount?: number;
  queuePosition?: number;
  myVote?: boolean;
  voteCount?: number;
  voteCooldown?: number;
}

export interface BroadcastInfo {
  currentSong: Song;
  nextSong: Song;
}
