-- Run this SQL to add the IsHost column to the MultiplayerPlayers table
-- This is needed for the host-as-player feature

ALTER TABLE MultiplayerPlayers ADD COLUMN IsHost INTEGER NOT NULL DEFAULT 0;
