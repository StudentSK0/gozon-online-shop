const paymentsOutput = document.getElementById("paymentsOutput");
const ordersOutput = document.getElementById("ordersOutput");
const balanceValue = document.getElementById("balanceValue");
const balanceMeta = document.getElementById("balanceMeta");
const paymentsStatus = document.getElementById("paymentsStatus");
const ordersStatus = document.getElementById("ordersStatus");
const lastOrderId = document.getElementById("lastOrderId");
const lastOrderStatus = document.getElementById("lastOrderStatus");
const ordersList = document.getElementById("ordersList");
const pageSummary = document.getElementById("pageSummary");
const pageInfo = document.getElementById("pageInfo");
const prevPageButton = document.getElementById("prevPage");
const nextPageButton = document.getElementById("nextPage");
const sessionStatus = document.getElementById("sessionStatus");
const userIdInput = document.getElementById("userId");
const topupAmountInput = document.getElementById("topupAmount");
const orderAmountInput = document.getElementById("orderAmount");
const orderDescriptionInput = document.getElementById("orderDescription");
const orderIdInput = document.getElementById("orderId");
const toast = document.getElementById("toast");
const createAccountButton = document.getElementById("createAccount");
const topupButton = document.getElementById("topup");
const createOrderButton = document.getElementById("createOrder");
const getOrderButton = document.getElementById("getOrder");
const saveUserIdButton = document.getElementById("saveUserId");
const clearUserIdButton = document.getElementById("clearUserId");

const endpoints = {
  payments: "/payments/api",
  orders: "/orders/api"
};

const storageKey = "gozonUserId";
const getUserId = () => userIdInput.value.trim();
const ordersState = {
  items: [],
  page: 1,
  pageSize: 4
};
let currentUserId = "";
let balanceIntervalId = null;
let ordersIntervalId = null;
let statusSocket = null;
let statusSocketOrderId = "";
const setUserId = (value) => {
  userIdInput.value = value;
  if (value) {
    localStorage.setItem(storageKey, value);
  } else {
    localStorage.removeItem(storageKey);
  }
  updateSessionStatus();
  updateActionStates();
};

const request = async (url, options = {}) => {
  const userId = getUserId();
  if (!userId) {
    throw new Error("Введите User ID");
  }

  const headers = Object.assign({
    "X-User-Id": userId
  }, options.headers || {});

  const response = await fetch(url, {
    ...options,
    headers
  });

  const text = await response.text();
  let data = {};
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = { message: text };
    }
  }

  if (!response.ok) {
    const message = data.error || data.message || `HTTP ${response.status}`;
    throw new Error(message);
  }

  return data;
};

const setOutput = (element, data) => {
  element.textContent = typeof data === "string" ? data : JSON.stringify(data, null, 2);
};

const showToast = (message) => {
  toast.textContent = message;
  toast.classList.add("show");
  setTimeout(() => toast.classList.remove("show"), 2400);
};

const setStatusPill = (element, text, isActive = false) => {
  element.textContent = text;
  element.classList.toggle("active", isActive);
};

const setBadge = (element, status) => {
  element.textContent = status || "нет данных";
  element.classList.remove("ok", "warn", "neutral");
  if (status === "FINISHED") {
    element.classList.add("ok");
  } else if (status === "CANCELLED") {
    element.classList.add("warn");
  } else {
    element.classList.add("neutral");
  }
};

const setBalance = (balance) => {
  if (balance === undefined || balance === null) {
    balanceValue.textContent = "—";
    balanceMeta.textContent = "нет данных";
    return;
  }
  balanceValue.textContent = `${balance} ₽`;
  balanceMeta.textContent = `обновлено ${new Date().toLocaleTimeString("ru-RU")}`;
};

const updateSessionStatus = () => {
  const userId = getUserId();
  if (userId) {
    sessionStatus.textContent = userId;
    sessionStatus.classList.add("active");
  } else {
    sessionStatus.textContent = "не задана";
    sessionStatus.classList.remove("active");
  }

  if (userId !== currentUserId) {
    currentUserId = userId;
    closeStatusSocket();
    resetUiForUser();
    startAutoRefresh();
    if (userId) {
      fetchBalance(true).catch(() => {});
      refreshOrders().catch(() => {});
    }
  }
};

const updateActionStates = () => {
  const hasUserId = Boolean(getUserId());
  const topupAmount = Number(topupAmountInput.value);
  const orderAmount = Number(orderAmountInput.value);
  const hasDescription = Boolean(orderDescriptionInput.value.trim());
  const hasOrderId = Boolean(orderIdInput.value.trim());

  createAccountButton.disabled = !hasUserId;
  topupButton.disabled = !hasUserId || !Number.isFinite(topupAmount) || topupAmount <= 0;
  createOrderButton.disabled = !hasUserId || !Number.isFinite(orderAmount) || orderAmount <= 0 || !hasDescription;
  getOrderButton.disabled = !hasUserId || !hasOrderId;
};

const normalizeTime = (value) => {
  if (!value) {
    return 0;
  }
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? 0 : parsed;
};

const resetUiForUser = () => {
  setBalance(null);
  setOrdersList([]);
  lastOrderId.textContent = "—";
  setBadge(lastOrderStatus, "");
  paymentsOutput.textContent = "";
  ordersOutput.textContent = "";
};

const closeStatusSocket = () => {
  if (statusSocket) {
    statusSocket.close(1000, "Switching user");
    statusSocket = null;
    statusSocketOrderId = "";
  }
};

const stopAutoRefresh = () => {
  if (balanceIntervalId) {
    clearInterval(balanceIntervalId);
    balanceIntervalId = null;
  }
  if (ordersIntervalId) {
    clearInterval(ordersIntervalId);
    ordersIntervalId = null;
  }
};

const startAutoRefresh = () => {
  stopAutoRefresh();
  if (!getUserId()) {
    return;
  }
  balanceIntervalId = setInterval(() => {
    fetchBalance(true).catch(() => {});
  }, 6000);
  ordersIntervalId = setInterval(() => {
    refreshOrders().catch(() => {});
  }, 8000);
};

const connectStatusSocket = (orderId) => {
  const userId = getUserId();
  if (!userId || !orderId) {
    return;
  }

  if (statusSocket && statusSocketOrderId === orderId) {
    return;
  }

  closeStatusSocket();
  statusSocketOrderId = orderId;

  const scheme = window.location.protocol === "https:" ? "wss" : "ws";
  const wsUrl = `${scheme}://${window.location.host}/orders/ws?userId=${encodeURIComponent(userId)}&orderId=${encodeURIComponent(orderId)}`;

  statusSocket = new WebSocket(wsUrl);
  statusSocket.onopen = () => {
    showToast("Realtime подключен");
  };
  statusSocket.onmessage = (event) => {
    let payload = null;
    try {
      payload = JSON.parse(event.data);
    } catch {
      return;
    }

    if (!payload || !payload.orderId) {
      return;
    }

    lastOrderId.textContent = payload.orderId;
    setBadge(lastOrderStatus, payload.status);
    setOutput(ordersOutput, payload);
    showToast(`Статус заказа: ${payload.status}`);
    refreshOrders().catch(() => {});
  };
  statusSocket.onclose = () => {
    statusSocket = null;
  };
};

const renderOrdersPage = () => {
  const total = ordersState.items.length;
  const totalPages = Math.max(1, Math.ceil(total / ordersState.pageSize));
  if (ordersState.page > totalPages) {
    ordersState.page = totalPages;
  }
  if (ordersState.page < 1) {
    ordersState.page = 1;
  }

  const startIndex = (ordersState.page - 1) * ordersState.pageSize;
  const endIndex = Math.min(startIndex + ordersState.pageSize, total);
  const pageItems = ordersState.items.slice(startIndex, endIndex);

  ordersList.innerHTML = "";
  if (pageItems.length === 0) {
    ordersList.innerHTML = "<tr><td class=\"muted\" colspan=\"5\">Заказов пока нет</td></tr>";
  } else {
    pageItems.forEach((order) => {
      const row = document.createElement("tr");
      const statusClass = order.status === "FINISHED"
        ? "ok"
        : order.status === "CANCELLED"
          ? "warn"
          : "neutral";
      const createdAt = order.createdAt ? new Date(order.createdAt).toLocaleString("ru-RU") : "—";

      row.innerHTML = `
        <td>${order.description || "Без описания"}</td>
        <td>${order.amount} ₽</td>
        <td><span class="badge ${statusClass}">${order.status}</span></td>
        <td class="id-cell">${order.id}</td>
        <td>${createdAt}</td>
      `;
      row.addEventListener("click", () => {
        orderIdInput.value = order.id;
        lastOrderId.textContent = order.id;
        setBadge(lastOrderStatus, order.status);
        setOutput(ordersOutput, order);
        updateActionStates();
        connectStatusSocket(order.id);
      });
      ordersList.appendChild(row);
    });
  }

  pageSummary.textContent = `Показаны ${total === 0 ? 0 : startIndex + 1}-${endIndex} из ${total}`;
  pageInfo.textContent = `Страница ${ordersState.page} из ${totalPages}`;
  prevPageButton.disabled = ordersState.page <= 1;
  nextPageButton.disabled = ordersState.page >= totalPages;
};

const setOrdersList = (orders) => {
  ordersState.items = (orders || [])
    .slice()
    .sort((left, right) => normalizeTime(right.createdAt) - normalizeTime(left.createdAt));
  ordersState.page = 1;
  renderOrdersPage();
};

const fetchBalance = async (silent = false) => {
  const data = await request(`${endpoints.payments}/accounts/balance`);
  setBalance(data.balance);
  if (!silent) {
    setOutput(paymentsOutput, data);
  }
  return data;
};

const handleAction = (element, statusElement, action) => async () => {
  try {
    setStatusPill(statusElement, "working", true);
    const data = await action();
    setOutput(element, data);
    showToast("Готово");
  } catch (error) {
    setOutput(element, error.message);
    showToast(error.message);
  } finally {
    setStatusPill(statusElement, "idle", false);
  }
};

const refreshOrders = async () => {
  const orders = await request(`${endpoints.orders}/orders`);
  setOrdersList(orders);
  return orders;
};

const trackOrder = async (orderId) => {
  if (!orderId) {
    return;
  }
  lastOrderId.textContent = orderId;
  setBadge(lastOrderStatus, "NEW");
  for (let i = 0; i < 8; i += 1) {
    const order = await request(`${endpoints.orders}/orders/${orderId}`);
    setBadge(lastOrderStatus, order.status);
    if (order.status !== "NEW") {
      return order;
    }
    await new Promise((resolve) => setTimeout(resolve, 2000));
  }
  return null;
};

createAccountButton.addEventListener("click", handleAction(paymentsOutput, paymentsStatus, async () => {
  const data = await request(`${endpoints.payments}/accounts`, { method: "POST" });
  await fetchBalance(true);
  showToast("Счет создан");
  return data;
}));

topupButton.addEventListener("click", handleAction(paymentsOutput, paymentsStatus, async () => {
  const amount = Number(topupAmountInput.value);
  const data = await request(`${endpoints.payments}/accounts/topup`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ amount })
  });
  await new Promise((resolve) => setTimeout(resolve, 300));
  await fetchBalance(true);
  return data;
}));

createOrderButton.addEventListener("click", handleAction(ordersOutput, ordersStatus, async () => {
  const description = orderDescriptionInput.value.trim();
  const amount = Number(orderAmountInput.value);
  const data = await request(`${endpoints.orders}/orders`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ amount, description })
  });
  connectStatusSocket(data.orderId);
  const tracked = await trackOrder(data.orderId);
  await refreshOrders();
  return tracked || data;
}));

getOrderButton.addEventListener("click", handleAction(ordersOutput, ordersStatus, async () => {
  const orderId = orderIdInput.value.trim();
  if (!orderId) {
    throw new Error("Введите Order ID");
  }
  const data = await request(`${endpoints.orders}/orders/${orderId}`);
  lastOrderId.textContent = data.id || orderId;
  setBadge(lastOrderStatus, data.status);
  connectStatusSocket(orderId);
  return data;
}));

saveUserIdButton.addEventListener("click", () => {
  setUserId(getUserId());
  showToast("User ID сохранен");
});

clearUserIdButton.addEventListener("click", () => {
  setUserId("");
  showToast("User ID сброшен");
});

prevPageButton.addEventListener("click", () => {
  ordersState.page -= 1;
  renderOrdersPage();
});

nextPageButton.addEventListener("click", () => {
  ordersState.page += 1;
  renderOrdersPage();
});

userIdInput.addEventListener("input", () => {
  updateSessionStatus();
  updateActionStates();
});

topupAmountInput.addEventListener("input", updateActionStates);
orderAmountInput.addEventListener("input", updateActionStates);
orderDescriptionInput.addEventListener("input", updateActionStates);
orderIdInput.addEventListener("input", updateActionStates);

document.addEventListener("DOMContentLoaded", () => {
  const saved = localStorage.getItem(storageKey) || "";
  if (saved) {
    setUserId(saved);
  } else {
    updateSessionStatus();
  }
  updateActionStates();
});
