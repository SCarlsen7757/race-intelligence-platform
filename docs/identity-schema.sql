CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE person (
    id uuid NOT NULL,
    display_name text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_person" PRIMARY KEY (id)
);

CREATE TABLE person_sim_alias (
    id uuid NOT NULL,
    person_id uuid NOT NULL,
    sim_key text NOT NULL,
    sim_driver_id text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_person_sim_alias" PRIMARY KEY (id),
    CONSTRAINT "FK_person_sim_alias_person_person_id" FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE
);

CREATE INDEX "IX_person_display_name" ON person (display_name);

CREATE INDEX "IX_person_sim_alias_person_id" ON person_sim_alias (person_id);

CREATE UNIQUE INDEX "IX_person_sim_alias_sim_key_sim_driver_id" ON person_sim_alias (sim_key, sim_driver_id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260820161559_InitialCreate', '10.0.11');

COMMIT;

