-- Run once in the Neon SQL Editor (as the owner role, e.g. neondb_owner).
-- Existing databases created before autodesk_username existed: run migrations/001_autodesk_username.sql instead.

-- Revit only exposes the Autodesk *username* (shown in CTTools > About), not the email.
-- Newer Autodesk accounts use the email as username -> "email" alone is enough.
-- Older accounts have a separate username (e.g. "lechithien26") -> also fill "autodesk_username".
create table if not exists licenses (
    email              text primary key check (email = lower(email)),
    autodesk_username  text unique check (autodesk_username = lower(autodesk_username)),
    is_active          boolean     not null default true,
    expires_at         timestamptz,               -- null = never expires
    note               text,
    created_at         timestamptz not null default now()
);

revoke all on table licenses from public;

-- The only thing the add-in can do: ask "is this Autodesk account licensed?".
-- SECURITY DEFINER runs with the owner's rights, so the client role never touches the table directly.
-- p_email receives the Revit username (kept under this name so existing grants stay valid).
create or replace function check_license(p_email text)
returns boolean
language sql
stable
security definer
set search_path = public
as $$
    select exists (
        select 1
        from licenses
        where lower(trim(p_email)) in (email, autodesk_username)
          and is_active
          and (expires_at is null or expires_at > now())
    );
$$;

revoke all on function check_license(text) from public;

-- Role embedded in the add-in. Neon requires a strong password (12+ chars, mixed).
create role cttools_client with login password 'CHANGE_ME_to_a_long_random_password';
grant usage on schema public to cttools_client;
grant execute on function check_license(text) to cttools_client;


-- ===== Everyday management (run as owner) =====
-- New account (username is the email):
--   insert into licenses (email, note) values ('client@firm.com', 'Trial');
-- Old account (username differs from email - ask the user for the value shown in CTTools > About):
--   insert into licenses (email, autodesk_username) values ('someone@gmail.com', 'someone_bim');
-- Time-limited:
--   insert into licenses (email, expires_at) values ('client@firm.com', now() + interval '1 year');
-- Revoke:
--   update licenses set is_active = false where email = 'someone@gmail.com';
--   select * from licenses order by created_at desc;
