// settings.js 設定中心頁前端互動邏輯
// 資料由 Pages/Settings.cshtml 透過 window.__gfSettings 注入

(() =>
{
    // 讀取 cshtml 注入的資料 未注入時直接結束
    const settings = window.__gfSettings;
    if (!settings)
    {
        return;
    }
    const impactData = settings.impactData;
    const historyMonthMap = settings.historyMonthMap;
    const banksMap = settings.banksMap || {};                                  // BankCode → BankName 映射 用於 modal 輸入代碼即時預覽銀行名

    // 綁定銀行代碼 input 與 preview div 輸入時即時查 banksMap 顯示銀行名
    function AttachBankPreview(inputId, previewId)
    {
        const input = document.getElementById(inputId);
        const preview = document.getElementById(previewId);
        if (!input || !preview)
        {
            return;
        }

        // 寫入 preview 文字 找不到 / 空字串 顯示提示
        function Refresh()
        {
            const code = input.value.trim();
            if (code === "")
            {
                preview.textContent = "";
                preview.classList.remove("is-hit", "is-miss");
                return;
            }
            const name = banksMap[code];
            if (name)
            {
                preview.textContent = name;
                preview.classList.add("is-hit");
                preview.classList.remove("is-miss");
            }
            else
            {
                preview.textContent = "查無此代碼";
                preview.classList.add("is-miss");
                preview.classList.remove("is-hit");
            }
        }

        input.addEventListener("input", Refresh);
        input.addEventListener("change", Refresh);
        Refresh();                                                             // 初始 render 一次 處理開 modal 帶入既有 value 的情境
    }
    AttachBankPreview("baBankCode", "baBankPreview");
    AttachBankPreview("csBankCode", "csBankPreview");

    const historyYearSelect = document.getElementById("historyYearSelect");
    const historyMonthSelect = document.getElementById("historyMonthSelect");

    function getMonthListByYear(yearText)
    {
        const fromMap = historyMonthMap?.[yearText];

        if (Array.isArray(fromMap) && fromMap.length > 0)
        {
            return fromMap.map(Number).sort((a, b) => b - a);
        }

        return [];
    }

    function renderHistoryMonths(selectedMonthText)
    {
        if (!historyYearSelect || !historyMonthSelect)
        {
            return;
        }

        const monthList = getMonthListByYear(historyYearSelect.value);
        historyMonthSelect.innerHTML = "";

        monthList.forEach(month =>
        {
            const option = document.createElement("option");
            option.value = String(month);
            option.textContent = String(month).padStart(2, "0");
            historyMonthSelect.appendChild(option);
        });

        if (!selectedMonthText)
        {
            if (historyMonthSelect.options.length > 0)
            {
                historyMonthSelect.selectedIndex = 0;
            }

            return;
        }

        const targetValue = String(Number(selectedMonthText));
        const targetOption = Array.from(historyMonthSelect.options).find(x => x.value === targetValue);

        if (targetOption)
        {
            historyMonthSelect.value = targetOption.value;
        }
        else if (historyMonthSelect.options.length > 0)
        {
            historyMonthSelect.selectedIndex = 0;
        }
    }

    const initialHistoryMonth = historyMonthSelect?.value ?? "";
    renderHistoryMonths(initialHistoryMonth);
    historyYearSelect?.addEventListener("change", () => renderHistoryMonths(null));

    const bankModalEl = document.getElementById("bankAccountModal");
    const btnAddBank = document.getElementById("btnAddBankAccount");
    const baTitle = document.getElementById("bankAccountModalLabel");
    const baId = document.getElementById("baId");
    const baBankCode = document.getElementById("baBankCode");
    const baAccountName = document.getElementById("baAccountName");
    const baAccountType = document.getElementById("baAccountType");
    const baBalance = document.getElementById("baBalance");
    const baEnabled = document.getElementById("baEnabled");
    const baDisableWarning = document.getElementById("baDisableWarning");
    const baSubmitBtn = document.getElementById("baSubmitBtn");
    const baImpactBox = document.getElementById("baImpactBox");
    const baImpactSummary = document.getElementById("baImpactSummary");
    const baImpactDetails = document.getElementById("baImpactDetails");

    function renderImpact(accountId)
    {
        if (!baImpactBox || !baImpactSummary || !baImpactDetails)
        {
            return;
        }

        baImpactSummary.textContent = "";
        baImpactDetails.innerHTML = "";

        const impact = impactData.find(x =>
        {
            const id = x.bankAccountId ?? x.BankAccountId;
            return typeof id === "string" && id.toLowerCase() === String(accountId).toLowerCase();
        });

        if (!impact)
        {
            baImpactBox.classList.add("d-none");
            return;
        }

        const details = impact.details ?? impact.Details ?? [];
        const confirmedTotalAmount = impact.confirmedTotalAmount ?? impact.ConfirmedTotalAmount ?? 0;
        const unconfirmedCount = impact.unconfirmedCount ?? impact.UnconfirmedCount ?? 0;
        baImpactSummary.textContent = `已確認待扣總額 ${Number(confirmedTotalAmount).toLocaleString()} 元  未確認帳單 ${Number(unconfirmedCount)} 筆`;

        details.forEach(item =>
        {
            const li = document.createElement("li");
            const bankCode = item.bankCode ?? item.BankCode ?? "";
            const bankName = item.bankName ?? item.BankName ?? "";
            const billYear = Number(item.billYear ?? item.BillYear ?? 0);
            const billMonth = Number(item.billMonth ?? item.BillMonth ?? 0);
            const paymentDueDay = Number(item.paymentDueDay ?? item.PaymentDueDay ?? 0);
            const billAmount = item.billAmount ?? item.BillAmount;
            const amountConfirmed = item.amountConfirmed ?? item.AmountConfirmed;
            const bankText = bankName ? `${bankCode} ${bankName}` : bankCode;
            const amountText = amountConfirmed && billAmount !== null && billAmount !== undefined
                ? `${Number(billAmount).toLocaleString()} 元`
                : "未確認";
            li.textContent = `${bankText} | ${billYear}/${String(billMonth).padStart(2, "0")} | ${amountText} | 繳費日 ${String(billMonth).padStart(2, "0")}/${String(paymentDueDay).padStart(2, "0")}`;
            baImpactDetails.appendChild(li);
        });

        baImpactBox.classList.remove("d-none");
    }

    // 記錄目前編輯中的帳戶原本啟用狀態 用於判斷是否顯示停用警告
    let editingBankEnabledBefore = false;

    // 同步停用警告與儲存按鈕文字 僅在原本啟用但目前取消勾選時提示
    function syncBankDisableWarning()
    {
        if (!baDisableWarning || !baSubmitBtn) return;

        const shouldWarn = editingBankEnabledBefore && !baEnabled.checked;

        if (shouldWarn)
        {
            baDisableWarning.classList.remove("d-none");
            baSubmitBtn.textContent = "確認停用";
            baSubmitBtn.classList.remove("gf-btn-primary");
            baSubmitBtn.classList.add("gf-btn-warning");
        }
        else
        {
            baDisableWarning.classList.add("d-none");
            baSubmitBtn.textContent = "儲存";
            baSubmitBtn.classList.remove("gf-btn-warning");
            baSubmitBtn.classList.add("gf-btn-primary");
        }
    }

    baEnabled?.addEventListener("change", syncBankDisableWarning);

    btnAddBank?.addEventListener("click", () =>
    {
        baTitle.textContent = "新增銀行帳戶";
        baId.value = "";
        baBankCode.value = "";
        baAccountName.value = "";
        baAccountType.value = "digital";
        baBalance.value = "";
        baEnabled.checked = true;
        baImpactBox?.classList.add("d-none");
        editingBankEnabledBefore = false;
        syncBankDisableWarning();
        baBankCode.dispatchEvent(new Event("input"));                          // 通知 bank-preview 重 render 避免顯示上一次 modal 的銀行名
    });

    bankModalEl?.addEventListener("show.bs.modal", event =>
    {
        const trigger = event.relatedTarget;
        if (!trigger) return;

        const id = trigger.getAttribute("data-id");
        if (!id)
        {
            return;
        }

        baTitle.textContent = "編輯銀行帳戶";
        baId.value = id;
        baBankCode.value = trigger.getAttribute("data-bankcode") ?? "";
        baAccountName.value = trigger.getAttribute("data-accountname") ?? "";
        baAccountType.value = trigger.getAttribute("data-accounttype") ?? "digital";
        baBalance.value = "";
        const enabledText = trigger.getAttribute("data-enabled") ?? "true";
        const wasEnabled = enabledText === "True" || enabledText === "true";
        baEnabled.checked = wasEnabled;
        editingBankEnabledBefore = wasEnabled;
        syncBankDisableWarning();
        renderImpact(id);
        baBankCode.dispatchEvent(new Event("input"));                          // 通知 bank-preview 重 render
    });

    const csModalEl = document.getElementById("creditSettingModal");
    const btnAddCs = document.getElementById("btnAddCreditSetting");
    const csTitle = document.getElementById("creditSettingModalLabel");
    const csId = document.getElementById("csId");
    const csBankCode = document.getElementById("csBankCode");
    const csStatementDay = document.getElementById("csStatementDay");
    const csPaymentDueDay = document.getElementById("csPaymentDueDay");
    const csPaymentAccountId = document.getElementById("csPaymentAccountId");
    const csEnabled = document.getElementById("csEnabled");

    btnAddCs?.addEventListener("click", () =>
    {
        csTitle.textContent = "新增信用卡設定";
        csId.value = "";
        csBankCode.value = "";
        csStatementDay.value = "1";
        csPaymentDueDay.value = "1";
        csPaymentAccountId.value = "";
        csEnabled.checked = true;
        csBankCode.dispatchEvent(new Event("input"));                          // 通知 bank-preview 重 render
    });

    csModalEl?.addEventListener("show.bs.modal", event =>
    {
        const trigger = event.relatedTarget;
        if (!trigger) return;

        const id = trigger.getAttribute("data-id");
        if (!id)
        {
            return;
        }

        csTitle.textContent = "編輯信用卡設定";
        csId.value = id;
        csBankCode.value = trigger.getAttribute("data-bankcode") ?? "";
        csStatementDay.value = trigger.getAttribute("data-statementday") ?? "1";
        csPaymentDueDay.value = trigger.getAttribute("data-paymentdueday") ?? "1";
        csPaymentAccountId.value = trigger.getAttribute("data-paymentaccountid") ?? "";
        const enabledText = trigger.getAttribute("data-enabled") ?? "true";
        csEnabled.checked = enabledText === "True" || enabledText === "true";
        csBankCode.dispatchEvent(new Event("input"));                          // 通知 bank-preview 重 render
    });

    const billAmountModalEl = document.getElementById("billAmountModal");
    const cbBillId = document.getElementById("cbBillId");
    const cbBillAmount = document.getElementById("cbBillAmount");
    const cbError = document.getElementById("cbError");

    billAmountModalEl?.addEventListener("show.bs.modal", event =>
    {
        const trigger = event.relatedTarget;
        if (!trigger) return;

        const billId = trigger.getAttribute("data-billid");
        const billAmount = trigger.getAttribute("data-billamount");

        cbBillId.value = billId ?? "";
        cbBillAmount.value = billAmount ?? "0";
        if (cbError)
        {
            cbError.classList.add("d-none");
            cbError.textContent = "";
        }
    });

    const showError = (el, msg) =>
    {
        if (!el) return;
        el.textContent = msg || "儲存失敗";
        el.classList.remove("d-none");
    };

    const clearError = el =>
    {
        if (!el) return;
        el.textContent = "";
        el.classList.add("d-none");
    };

    async function ajaxSubmit(formEl, errorEl, modalEl)
    {
        clearError(errorEl);
        const submitBtn = formEl.querySelector('button[type="submit"]');
        if (submitBtn) submitBtn.disabled = true;

        try
        {
            const formData = new FormData(formEl);
            const res = await fetch(formEl.action || window.location.href,
            {
                method: "POST",
                body: formData,
                headers: { "X-Requested-With": "XMLHttpRequest" },
                credentials: "same-origin"
            });

            const data = await res.json().catch(() => null);
            if (!res.ok || !data?.ok)
            {
                showError(errorEl, data?.message || `儲存失敗 ${res.status}`);
                return;
            }

            const modal = bootstrap.Modal.getInstance(modalEl) || new bootstrap.Modal(modalEl);
            modal.hide();
            window.location.reload();
        }
        catch
        {
            showError(errorEl, "儲存失敗 請確認網路連線");
        }
        finally
        {
            if (submitBtn) submitBtn.disabled = false;
        }
    }

    const bankForm = document.getElementById("bankAccountForm");
    const baError = document.getElementById("baError");
    bankForm?.addEventListener("submit", e =>
    {
        e.preventDefault();
        ajaxSubmit(bankForm, baError, bankModalEl);
    });

    const csForm = document.getElementById("creditSettingForm");
    const csError = document.getElementById("csError");
    csForm?.addEventListener("submit", e =>
    {
        e.preventDefault();
        ajaxSubmit(csForm, csError, csModalEl);
    });

    const billAmountForm = document.getElementById("billAmountForm");
    billAmountForm?.addEventListener("submit", e =>
    {
        e.preventDefault();
        ajaxSubmit(billAmountForm, cbError, billAmountModalEl);
    });

    // modal 完全關閉後清空錯誤訊息 不論使用者按 X 點背景或 ESC 都會觸發 避免下次開啟時舊錯誤殘留
    bankModalEl?.addEventListener("hidden.bs.modal", () => clearError(baError));
    csModalEl?.addEventListener("hidden.bs.modal", () => clearError(csError));
    billAmountModalEl?.addEventListener("hidden.bs.modal", () => clearError(cbError));
})();
