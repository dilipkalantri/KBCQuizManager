-- Run this SQL in your PostgreSQL database to add the IsHost column
-- This is required for the host-as-player feature

ALTER TABLE "MultiplayerPlayers" ADD COLUMN "IsHost" boolean NOT NULL DEFAULT false;
