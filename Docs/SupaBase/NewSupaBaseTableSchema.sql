create table public.messages (
  id uuid not null default gen_random_uuid (),
  sender_id uuid not null,
  sender_name text not null,
  content text not null,
  created_at timestamp with time zone not null,
  recipient_name text not null default 'Unknown'::text,
  constraint messages_pkey primary key (id),
  constraint messages_sender_id_fkey foreign KEY (sender_id) references auth.users (id)
) TABLESPACE pg_default;