-- Adds autodesk_username so a license matches either the email or the Autodesk username reported by Revit.
-- Run once in the Neon SQL Editor as the owner role. Safe to re-run.

alter table licenses
    add column if not exists autodesk_username text unique
    check (autodesk_username = lower(autodesk_username));

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

-- Owner account
update licenses set autodesk_username = 'lechithien26' where email = 'lechithien26@gmail.com';

-- Should return true
select check_license('lechithien26') as by_username, check_license('lechithien26@gmail.com') as by_email;
