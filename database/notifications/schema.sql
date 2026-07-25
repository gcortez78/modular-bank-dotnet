--
-- PostgreSQL database dump
--

\restrict ljLE96blMeCrkBlTfD0PvJDEt5R4Gbd8OvabRgnM7sVPLLdLx2OPMQyfZZ3jJPQ

-- Dumped from database version 16.14
-- Dumped by pg_dump version 16.14

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: notifications; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA notifications;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: notifications; Owner: -
--

CREATE TABLE notifications."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


--
-- Name: notifications; Type: TABLE; Schema: notifications; Owner: -
--

CREATE TABLE notifications.notifications (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    type integer NOT NULL,
    payload jsonb NOT NULL,
    idempotency_key character varying(100),
    created_at timestamp with time zone NOT NULL
);


--
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: notifications; Owner: -
--

ALTER TABLE ONLY notifications."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");


--
-- Name: notifications pk_notifications; Type: CONSTRAINT; Schema: notifications; Owner: -
--

ALTER TABLE ONLY notifications.notifications
    ADD CONSTRAINT pk_notifications PRIMARY KEY (id);


--
-- Name: ix_notifications_user_created_at; Type: INDEX; Schema: notifications; Owner: -
--

CREATE INDEX ix_notifications_user_created_at ON notifications.notifications USING btree (user_id, created_at);


--
-- Name: ux_notifications_idempotency_key; Type: INDEX; Schema: notifications; Owner: -
--

CREATE UNIQUE INDEX ux_notifications_idempotency_key ON notifications.notifications USING btree (idempotency_key) WHERE (idempotency_key IS NOT NULL);


--
-- PostgreSQL database dump complete
--

\unrestrict ljLE96blMeCrkBlTfD0PvJDEt5R4Gbd8OvabRgnM7sVPLLdLx2OPMQyfZZ3jJPQ

