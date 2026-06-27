--
-- PostgreSQL database dump
--

-- Dumped from database version 16.14 (7f2ba1b)
-- Dumped by pg_dump version 18.1

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: SCHEMA public; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON SCHEMA public IS '';


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: bank_accounts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.bank_accounts (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    bank_code character varying(10) NOT NULL,
    account_name text NOT NULL,
    account_type text NOT NULL,
    balance integer DEFAULT 0 NOT NULL,
    enabled boolean DEFAULT true NOT NULL,
    balance_updated_at timestamp with time zone,
    CONSTRAINT ck_bank_accounts_account_type CHECK ((account_type = ANY (ARRAY['digital'::text, 'physical'::text]))),
    CONSTRAINT ck_bank_accounts_balance CHECK (((balance)::numeric >= (0)::numeric))
);


--
-- Name: banks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.banks (
    bank_code character varying(10) NOT NULL,
    bank_name text NOT NULL,
    CONSTRAINT ck_banks_bank_code CHECK (((bank_code)::text ~ '^[0-9]{3,10}$'::text))
);


--
-- Name: credit_bills; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.credit_bills (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    bank_code character varying(10) NOT NULL,
    bill_year integer NOT NULL,
    bill_month integer NOT NULL,
    statement_day integer NOT NULL,
    payment_due_day integer NOT NULL,
    bill_amount integer,
    amount_confirmed boolean DEFAULT false NOT NULL,
    paid boolean DEFAULT false NOT NULL,
    CONSTRAINT ck_credit_bills_bill_amount CHECK (((bill_amount IS NULL) OR (bill_amount >= 0))),
    CONSTRAINT ck_credit_bills_bill_month CHECK (((bill_month >= 1) AND (bill_month <= 12))),
    CONSTRAINT ck_credit_bills_bill_year CHECK (((bill_year >= 2000) AND (bill_year <= 9999))),
    CONSTRAINT ck_credit_bills_payment_due_day CHECK (((payment_due_day >= 1) AND (payment_due_day <= 31))),
    CONSTRAINT ck_credit_bills_statement_day CHECK (((statement_day >= 1) AND (statement_day <= 31)))
);


--
-- Name: credit_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.credit_settings (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    bank_code character varying(10) NOT NULL,
    statement_day integer NOT NULL,
    payment_due_day integer NOT NULL,
    payment_bank_account_id uuid,
    enabled boolean DEFAULT true NOT NULL,
    CONSTRAINT ck_credit_settings_payment_due_day CHECK (((payment_due_day >= 1) AND (payment_due_day <= 31))),
    CONSTRAINT ck_credit_settings_statement_day CHECK (((statement_day >= 1) AND (statement_day <= 31)))
);


--
-- Name: notification_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.notification_logs (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    notification_type text NOT NULL,
    target_id uuid,
    message_content text NOT NULL,
    status text NOT NULL,
    error_message text,
    sent_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_notification_logs_status CHECK ((status = ANY (ARRAY['success'::text, 'fail'::text])))
);


--
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.users (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name text NOT NULL,
    discord_user_id text,
    discord_private_channel_id text
);


--
-- Name: web_link_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.web_link_tokens (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    token_hash text NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    used_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: bank_accounts bank_accounts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bank_accounts
    ADD CONSTRAINT bank_accounts_pkey PRIMARY KEY (id);


--
-- Name: banks banks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.banks
    ADD CONSTRAINT banks_pkey PRIMARY KEY (bank_code);


--
-- Name: credit_bills credit_bills_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_bills
    ADD CONSTRAINT credit_bills_pkey PRIMARY KEY (id);


--
-- Name: credit_settings credit_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_settings
    ADD CONSTRAINT credit_settings_pkey PRIMARY KEY (id);


--
-- Name: notification_logs notification_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_logs
    ADD CONSTRAINT notification_logs_pkey PRIMARY KEY (id);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- Name: web_link_tokens web_link_tokens_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_link_tokens
    ADD CONSTRAINT web_link_tokens_pkey PRIMARY KEY (id);


--
-- Name: ix_bank_accounts_bank_code; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_bank_accounts_bank_code ON public.bank_accounts USING btree (bank_code);


--
-- Name: ix_bank_accounts_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_bank_accounts_user_id ON public.bank_accounts USING btree (user_id);


--
-- Name: ix_bank_accounts_user_id_bank_code; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_bank_accounts_user_id_bank_code ON public.bank_accounts USING btree (user_id, bank_code);


--
-- Name: ix_credit_bills_payment_due_day; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_credit_bills_payment_due_day ON public.credit_bills USING btree (payment_due_day);


--
-- Name: ix_credit_bills_statement_day; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_credit_bills_statement_day ON public.credit_bills USING btree (statement_day);


--
-- Name: ix_credit_bills_user_id_bank_code; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_credit_bills_user_id_bank_code ON public.credit_bills USING btree (user_id, bank_code);


--
-- Name: ix_credit_settings_bank_code; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_credit_settings_bank_code ON public.credit_settings USING btree (bank_code);


--
-- Name: ix_credit_settings_payment_bank_account_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_credit_settings_payment_bank_account_id ON public.credit_settings USING btree (payment_bank_account_id);


--
-- Name: ix_notification_logs_notification_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_notification_logs_notification_type ON public.notification_logs USING btree (notification_type);


--
-- Name: ix_notification_logs_sent_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_notification_logs_sent_at ON public.notification_logs USING btree (sent_at DESC);


--
-- Name: ix_notification_logs_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_notification_logs_user_id ON public.notification_logs USING btree (user_id);


--
-- Name: ix_notification_logs_user_id_sent_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_notification_logs_user_id_sent_at ON public.notification_logs USING btree (user_id, sent_at DESC);


--
-- Name: ux_banks_bank_name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_banks_bank_name ON public.banks USING btree (bank_name);


--
-- Name: ux_credit_bills_user_id_bank_code_bill_year_bill_month; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_credit_bills_user_id_bank_code_bill_year_bill_month ON public.credit_bills USING btree (user_id, bank_code, bill_year, bill_month);


--
-- Name: ux_credit_settings_user_id_bank_code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_credit_settings_user_id_bank_code ON public.credit_settings USING btree (user_id, bank_code);


--
-- Name: ux_users_discord_private_channel_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_users_discord_private_channel_id ON public.users USING btree (discord_private_channel_id) WHERE (discord_private_channel_id IS NOT NULL);


--
-- Name: ux_users_discord_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_users_discord_user_id ON public.users USING btree (discord_user_id) WHERE (discord_user_id IS NOT NULL);


--
-- Name: ux_web_link_tokens_one_active_per_user; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_web_link_tokens_one_active_per_user ON public.web_link_tokens USING btree (user_id) WHERE (used_at IS NULL);


--
-- Name: ux_web_link_tokens_token_hash; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_web_link_tokens_token_hash ON public.web_link_tokens USING btree (token_hash);


--
-- Name: bank_accounts fk_bank_accounts_banks; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bank_accounts
    ADD CONSTRAINT fk_bank_accounts_banks FOREIGN KEY (bank_code) REFERENCES public.banks(bank_code) ON DELETE RESTRICT;


--
-- Name: bank_accounts fk_bank_accounts_users; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bank_accounts
    ADD CONSTRAINT fk_bank_accounts_users FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: credit_bills fk_credit_bills_banks; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_bills
    ADD CONSTRAINT fk_credit_bills_banks FOREIGN KEY (bank_code) REFERENCES public.banks(bank_code) ON DELETE RESTRICT;


--
-- Name: credit_bills fk_credit_bills_users; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_bills
    ADD CONSTRAINT fk_credit_bills_users FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: credit_settings fk_credit_settings_banks; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_settings
    ADD CONSTRAINT fk_credit_settings_banks FOREIGN KEY (bank_code) REFERENCES public.banks(bank_code) ON DELETE RESTRICT;


--
-- Name: credit_settings fk_credit_settings_payment_bank_accounts; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_settings
    ADD CONSTRAINT fk_credit_settings_payment_bank_accounts FOREIGN KEY (payment_bank_account_id) REFERENCES public.bank_accounts(id) ON DELETE SET NULL;


--
-- Name: credit_settings fk_credit_settings_users; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_settings
    ADD CONSTRAINT fk_credit_settings_users FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: notification_logs fk_notification_logs_users; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_logs
    ADD CONSTRAINT fk_notification_logs_users FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: web_link_tokens web_link_tokens_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.web_link_tokens
    ADD CONSTRAINT web_link_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

