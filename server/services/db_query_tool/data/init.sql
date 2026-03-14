CREATE TABLE IF NOT EXISTS customers (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    segment TEXT NOT NULL,
    country TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS orders (
    id INTEGER PRIMARY KEY,
    customer_id INTEGER NOT NULL,
    amount REAL NOT NULL,
    currency TEXT NOT NULL,
    status TEXT NOT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (customer_id) REFERENCES customers(id)
);

INSERT OR IGNORE INTO customers (id, name, segment, country) VALUES
    (1, 'Acme Corp', 'enterprise', 'US'),
    (2, 'Blue Retail', 'mid-market', 'DE'),
    (3, 'Nova Labs', 'startup', 'PL'),
    (4, 'Sunrise Foods', 'enterprise', 'US'),
    (5, 'Pixel Works', 'startup', 'UK');

INSERT OR IGNORE INTO orders (id, customer_id, amount, currency, status, created_at) VALUES
    (1001, 1, 2499.00, 'USD', 'paid', '2026-01-11'),
    (1002, 2, 850.50, 'EUR', 'pending', '2026-01-12'),
    (1003, 3, 199.99, 'EUR', 'paid', '2026-01-13'),
    (1004, 4, 4200.00, 'USD', 'paid', '2026-01-13'),
    (1005, 5, 120.00, 'GBP', 'cancelled', '2026-01-14');
